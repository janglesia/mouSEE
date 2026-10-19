using System.Diagnostics;
using app.Calibration;

namespace app.Gaze;

/// <summary>The re-center's shift, subtracted from every bubble position (0-1 of the screen, + is too far right / down).</summary>
public sealed record GazeOffset(double X, double Y)
{
    public static readonly GazeOffset None = new(0, 0);
}

/// <summary>
/// Where the head is: the middle of the face in the camera image (0-1, mirrored so leaning to your right makes FaceX
/// bigger, and sliding down in the chair makes FaceY bigger), how wide the face is (bigger = closer), and the head
/// pitch in degrees. FaceY, FaceW and Pitch are null when not known (older calibrations).
/// </summary>
public sealed record HeadPosition(double FaceX, double? FaceY, double? FaceW, double? Pitch)
{
    public static HeadPosition? Of(IReadOnlyList<FrameSample> frames)
    {
        var face = frames.Where(f => f.FaceFound && f.FaceW > 0).ToList();
        if (face.Count == 0) return null;
        double Median(Func<FrameSample, double> pick) => Stats.Percentile(face.Select(pick).ToList(), 50);
        return new HeadPosition(Median(f => f.FaceX), Median(f => f.FaceY), Median(f => f.FaceW), Median(f => f.Pitch));
    }

    /// <summary>Where the head was during a calibration's nine targets, null if it doesn't have the face position.</summary>
    public static HeadPosition? Of(CalibrationProfile? calibration)
    {
        var nine = calibration?.GazeTargets.Where(t => !t.IsValidation && !t.MoveHead && t.Pose is null && t.FaceX is not null).ToList();
        if (nine is null || nine.Count == 0) return null;
        double? Median(Func<GazeTargetSummary, double?> pick)
        {
            var values = nine.Select(pick).OfType<double>().ToList();
            return values.Count == 0 ? null : Stats.Percentile(values, 50);
        }
        return new HeadPosition(Median(t => t.FaceX!.Median)!.Value, Median(t => t.FaceY?.Median), Median(t => t.FaceW), Median(t => t.Pitch?.Median));
    }
}

/// <summary>
/// Watches where the head is and tells the overlay two things. MoveHint: the head is so far from where it was during
/// calibration that tracking gets worse, and which way to go ("Move a little to your left"). SuggestRecenter: the head
/// moved since the last re-center (or the calibration) by enough that the bubble is probably a bit off, but not so
/// far that it has to move back. Both only after it lasted a moment, so moving around briefly doesn't flash anything.
/// Feed it every frame with OnFrame. No UI here.
/// </summary>
public sealed class PositionGuide
{
    // too far from the calibration: the fit only knows the head around where it was then. share of the camera image,
    // and how much bigger / smaller the face is (closer / further)
    public const double MoveX = 0.10, MoveY = 0.10, CloserRatio = 1.18, FurtherRatio = 0.85;
    // moved enough since the last re-center that one is worth doing
    public const double RecenterX = 0.05, RecenterY = 0.05, RecenterSizeChange = 0.08, RecenterPitch = 4;
    public const long MoveAfterMs = 1500, RecenterAfterMs = 4000;
    // a hint goes away once the head is back inside this share of the limit, so it doesn't flicker right at it
    public const double ClearAt = 0.8;

    private readonly Func<long> _nowMs;
    private string? _pendingMove;
    private long _moveSince = -1, _recenterSince = -1;

    public HeadPosition? Calibrated { get; set; }
    public HeadPosition? LastRecenter { get; set; }

    public string? MoveHint { get; private set; }
    public bool SuggestRecenter { get; private set; }
    public event Action? Changed;

    // nowMs is only for tests, normally a stopwatch
    public PositionGuide(Func<long>? nowMs = null)
    {
        var clock = Stopwatch.StartNew();
        _nowMs = nowMs ?? (() => clock.ElapsedMilliseconds);
    }

    /// <summary>A re-center just happened at this position: no suggestion until the head moves away from it.</summary>
    public void Recentered(HeadPosition? at)
    {
        LastRecenter = at;
        _recenterSince = -1;
        Set(MoveHint, false);
    }

    public void OnFrame(FrameSample f)
    {
        if (!f.FaceFound || f.FaceW <= 0 || Calibrated is null) return;
        long now = _nowMs();
        var here = new HeadPosition(f.FaceX, f.FaceY, f.FaceW, f.Pitch);

        // ---- too far from the calibration
        string? move = MoveFrom(Calibrated, here, MoveHint is null ? 1 : ClearAt);
        string? hint = MoveHint;
        if (move is null)
        {
            _pendingMove = null;
            _moveSince = -1;
            hint = null;
        }
        else if (move != _pendingMove)
        {
            _pendingMove = move;
            _moveSince = now;
        }
        else if (now - _moveSince >= MoveAfterMs)
        {
            hint = move;
        }

        // ---- moved since the last re-center (only when it doesn't have to move back anyway)
        bool recenter = SuggestRecenter;
        if (hint is null && Drifted(LastRecenter ?? Calibrated, here))
        {
            if (_recenterSince < 0) _recenterSince = now;
            if (now - _recenterSince >= RecenterAfterMs) recenter = true;
        }
        else if (hint is null)
        {
            _recenterSince = -1;
            recenter = false;
        }

        Set(hint, recenter);
    }

    /// <summary>Which way to move to get back to where calibration was, the biggest problem first. null if close enough.</summary>
    public static string? MoveFrom(HeadPosition calibrated, HeadPosition here, double scale = 1)
    {
        var problems = new List<(double Over, string Hint)>();
        double dx = here.FaceX - calibrated.FaceX;
        if (Math.Abs(dx) > MoveX * scale)
            problems.Add((Math.Abs(dx) / MoveX, dx > 0 ? "Move a little to your left" : "Move a little to your right"));
        if (here.FaceY is { } y && calibrated.FaceY is { } y0 && Math.Abs(y - y0) > MoveY * scale)
            problems.Add((Math.Abs(y - y0) / MoveY, y > y0 ? "Sit up a little" : "Sit a little lower"));
        if (here.FaceW is { } w && calibrated.FaceW is { } w0 && w0 > 0)
        {
            double ratio = w / w0;
            double closer = 1 + (CloserRatio - 1) * scale, further = 1 - (1 - FurtherRatio) * scale;
            if (ratio > closer) problems.Add(((ratio - 1) / (CloserRatio - 1), "Move back a little, you're too close to the camera"));
            else if (ratio < further) problems.Add(((1 - ratio) / (1 - FurtherRatio), "Move a little closer to the camera"));
        }
        return problems.Count == 0 ? null : problems.MaxBy(p => p.Over).Hint;
    }

    /// <summary>Moved enough from where the last re-center (or the calibration) was that the bubble is probably a bit off.</summary>
    public static bool Drifted(HeadPosition reference, HeadPosition here) =>
        Math.Abs(here.FaceX - reference.FaceX) > RecenterX
        || (here.FaceY is { } y && reference.FaceY is { } y0 && Math.Abs(y - y0) > RecenterY)
        || (here.FaceW is { } w && reference.FaceW is { } w0 && w0 > 0 && Math.Abs(w / w0 - 1) > RecenterSizeChange)
        || (here.Pitch is { } p && reference.Pitch is { } p0 && Math.Abs(p - p0) > RecenterPitch);

    private void Set(string? hint, bool recenter)
    {
        if (hint == MoveHint && recenter == SuggestRecenter) return;
        MoveHint = hint;
        SuggestRecenter = recenter;
        Changed?.Invoke();
    }
}
