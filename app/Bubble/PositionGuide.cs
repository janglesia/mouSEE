using System.Diagnostics;

namespace app.Bubble;

/// <summary>
/// Watches where the head is and tells the overlay two things. MoveHint: the head is so far from where it was during
/// calibration that tracking gets worse, and which way to go ("Move a little to your left"). SuggestRecenter: the head
/// moved since the last re-center (or the calibration) by enough that the bubble is probably a bit off, but not so
/// far that it has to move back. Both only after it lasted a moment, so moving around briefly doesn't flash anything.
/// Feed it every frame with OnFrame. No UI here.
/// </summary>
public sealed class PositionGuide
{
    // too far from the calibration: the fit only knows the head around where it was then, and small moves already
    // throw the bubble off. how far, as a share of the camera image (setting overlay.positionHintPercent): to the side
    // or up/down that far, or the face bigger / smaller by 1.8 / 1.5 times that (closer / further). It was 10% at first,
    // which let the head get far enough off that the bubble was clearly wrong before it said anything
    public const double DefaultMoveAt = 0.06;
    public const double CloserPerMove = 1.8, FurtherPerMove = 1.5;
    // and the head tilted up or down this many degrees (overlay.tiltHintDegrees). reading the bottom of the screen alone
    // tilts it 3-4 degrees (the face mesh changes with the lids), so not much less than this
    public const double DefaultTiltAt = 8;
    // moved enough since the last re-center that one is worth doing
    // pitch: the tracker's head pitch jitters by up to 4° sitting still, and shifts 3-4° just from looking at the bottom
    // of the screen (the face mesh changes with the lids), so it takes more than that
    public const double RecenterX = 0.05, RecenterY = 0.05, RecenterSizeChange = 0.08, RecenterPitch = 8;
    public const long MoveAfterMs = 1000, RecenterAfterMs = 8000;
    // the head position is the median of this long, so single jittery frames don't count
    public const long SmoothMs = 1500;
    // a hint goes away once the head is back inside this share of the limit, so it doesn't flicker right at it
    public const double ClearAt = 0.8;

    private readonly Func<long> _nowMs;
    private string? _pendingMove;
    private long _moveSince = -1, _recenterSince = -1;
    private readonly Queue<(long Ms, HeadPosition At)> _recent = new();

    public HeadPosition? Calibrated { get; set; }
    /// <summary>How far from the calibration before saying which way to go back (share of the camera image).</summary>
    public double MoveAt { get; set; } = DefaultMoveAt;
    /// <summary>How far the head can tilt from the calibration before saying to tilt it back (degrees).</summary>
    public double TiltAt { get; set; } = DefaultTiltAt;
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
        _recent.Enqueue((now, new HeadPosition(f.FaceX, f.FaceY, f.FaceW, f.Pitch)));
        while (_recent.Count > 0 && now - _recent.Peek().Ms > SmoothMs) _recent.Dequeue();
        var here = Median(_recent.Select(r => r.At).ToList());

        // ---- too far from the calibration
        string? move = MoveFrom(Calibrated, here, MoveHint is null ? 1 : ClearAt, MoveAt, TiltAt);
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

    /// <summary>Which way to move to get back to where calibration was, the biggest problem first. null if close enough.
    /// scale: a share of the limits (ClearAt once a hint is up). moveAt, tiltAt: the limits, see MoveAt and TiltAt.</summary>
    public static string? MoveFrom(HeadPosition calibrated, HeadPosition here, double scale = 1,
                                   double moveAt = DefaultMoveAt, double tiltAt = DefaultTiltAt)
    {
        var problems = new List<(double Over, string Hint)>();
        double dx = here.FaceX - calibrated.FaceX;
        if (Math.Abs(dx) > moveAt * scale)
            problems.Add((Math.Abs(dx) / moveAt, dx > 0 ? "Move a little to your left" : "Move a little to your right"));
        if (here.FaceY is { } y && calibrated.FaceY is { } y0 && Math.Abs(y - y0) > moveAt * scale)
            problems.Add((Math.Abs(y - y0) / moveAt, y > y0 ? "Sit up a little" : "Sit a little lower"));
        if (here.FaceW is { } w && calibrated.FaceW is { } w0 && w0 > 0)
        {
            double ratio = w / w0, closerAt = CloserPerMove * moveAt, furtherAt = FurtherPerMove * moveAt;
            if (ratio > 1 + closerAt * scale) problems.Add(((ratio - 1) / closerAt, "Move back a little, you're too close to the camera"));
            else if (ratio < 1 - furtherAt * scale) problems.Add(((1 - ratio) / furtherAt, "Move a little closer to the camera"));
        }
        // the tracker's pitch goes up tilting the head down
        if (here.Pitch is { } p && calibrated.Pitch is { } p0 && Math.Abs(p - p0) > tiltAt * scale)
            problems.Add((Math.Abs(p - p0) / tiltAt, p > p0 ? "Tilt your head up a little" : "Tilt your head down a little"));
        return problems.Count == 0 ? null : problems.MaxBy(p => p.Over).Hint;
    }

    /// <summary>Moved enough from where the last re-center (or the calibration) was that the bubble is probably a bit off.</summary>
    public static bool Drifted(HeadPosition reference, HeadPosition here) =>
        Math.Abs(here.FaceX - reference.FaceX) > RecenterX
        || (here.FaceY is { } y && reference.FaceY is { } y0 && Math.Abs(y - y0) > RecenterY)
        || (here.FaceW is { } w && reference.FaceW is { } w0 && w0 > 0 && Math.Abs(w / w0 - 1) > RecenterSizeChange)
        || (here.Pitch is { } p && reference.Pitch is { } p0 && Math.Abs(p - p0) > RecenterPitch);

    private static HeadPosition Median(IReadOnlyList<HeadPosition> positions)
    {
        double M(Func<HeadPosition, double> pick) => Stats.Percentile(positions.Select(pick).ToList(), 50);
        double? N(Func<HeadPosition, double?> pick)
        {
            var values = positions.Select(pick).OfType<double>().ToList();
            return values.Count == 0 ? null : Stats.Percentile(values, 50);
        }
        return new HeadPosition(M(p => p.FaceX), N(p => p.FaceY), N(p => p.FaceW), N(p => p.Pitch));
    }

    private void Set(string? hint, bool recenter)
    {
        if (hint == MoveHint && recenter == SuggestRecenter) return;
        MoveHint = hint;
        SuggestRecenter = recenter;
        Changed?.Invoke();
    }
}
