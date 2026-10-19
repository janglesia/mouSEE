using System.Diagnostics;

namespace app.LookToAnswer;

public enum LookSide { None, Left, Right }

/// <summary>
/// Yes / no without hands: look to the right for yes, to the left for no, and hold it for HoldMs.
/// Only the eyes count, from the tracker's gaze direction (eyes.gaze.h, + is the person's right). Not head turns:
/// turning the head right while keeping the eyes on the screen turns the eyes left in their sockets, so mixing
/// the two could read a yes as a no. Feed it every frame message with OnFrame.
/// </summary>
public sealed class LookChoice
{
    public const long HoldMs = 3000;
    // how far the iris has to be off center (-1 to 1 across the eye), the same as "left" / "right" in the
    // tracker's debug readout (H_THRESH in eye_extractor.py)
    public const double Threshold = 0.12;
    // staying on a side once it's being held takes less, see SideOf
    public const double StayThreshold = 0.08;
    // a blink or one bad frame in the middle of holding doesn't start the count over
    public const long DropoutMs = 400;

    private readonly Func<long> _nowMs;
    private bool _armed;          // eyes were back in the middle since the last choice (or since Start)
    private LookSide _holding = LookSide.None;
    private long _since, _lastSeen;

    /// <summary>The side being looked at right now, None when the eyes are in the middle or can't be seen.</summary>
    public LookSide Side { get; private set; }

    /// <summary>0-1, how long the current side has been held.</summary>
    public double Progress { get; private set; }

    /// <summary>False until the eyes have been in the middle once, so a question can't be answered before it was read.</summary>
    public bool Armed => _armed;

    /// <summary>Fires once per choice: true for yes (right), false for no (left).</summary>
    public event Action<bool>? Chosen;

    /// <summary>Side or Progress changed, for redrawing.</summary>
    public event Action? Changed;

    // nowMs is only for tests, normally a stopwatch
    public LookChoice(Func<long>? nowMs = null)
    {
        var clock = Stopwatch.StartNew();
        _nowMs = nowMs ?? (() => clock.ElapsedMilliseconds);
    }

    /// <summary>Starts over: the eyes have to come back to the middle before anything counts. Call when a new question shows up.</summary>
    public void Reset()
    {
        _armed = false;
        _holding = LookSide.None;
        Set(LookSide.None, 0);
    }

    public void OnFrame(TrackerMessage msg)
    {
        if (msg.Type != "frame") return;
        long now = _nowMs();
        double? h = Gaze(msg)?.H;
        var side = SideOf(h, _holding);

        if (side == LookSide.None)
        {
            // eyes that can't be seen (a blink, a bad frame) while holding: keep counting for a moment
            if (h is null && _holding != LookSide.None && now - _lastSeen <= DropoutMs) return;
            // only a real look back to the middle arms it
            if (h is not null) _armed = true;
            _holding = LookSide.None;
            Set(LookSide.None, 0);
            return;
        }

        if (!_armed) return;
        if (side != _holding)
        {
            _holding = side;
            _since = now;
        }
        _lastSeen = now;

        double progress = Math.Min(1, (now - _since) / (double)HoldMs);
        Set(side, progress);
        if (progress < 1) return;

        // chosen. the eyes have to come back to the middle before the next one
        _armed = false;
        _holding = LookSide.None;
        Set(LookSide.None, 0);
        Chosen?.Invoke(side == LookSide.Right);
    }

    // once a side is being held it takes a smaller offset to stay on it, so eyes right at the threshold don't
    // flicker between the side and the middle and start the count over each time
    private static LookSide SideOf(double? h, LookSide holding)
    {
        if (h is null) return LookSide.None;
        double right = holding == LookSide.Right ? StayThreshold : Threshold;
        double left = holding == LookSide.Left ? StayThreshold : Threshold;
        return h > right ? LookSide.Right : h < -left ? LookSide.Left : LookSide.None;
    }

    private static GazeData? Gaze(TrackerMessage msg) => msg.Faces is { Count: > 0 } faces ? faces[0].Eyes?.Gaze : null;

    private void Set(LookSide side, double progress)
    {
        if (side == Side && Math.Abs(progress - Progress) < 0.005) return;
        Side = side;
        Progress = progress;
        Changed?.Invoke();
    }
}
