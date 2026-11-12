namespace app.Input;

/// <summary>
/// Dwell: looking at one target long enough picks it. The timer starts when the gaze comes onto a target and keeps going
/// while it stays there. It only starts over once the gaze has been off the target for leaveMs, so one stray frame (the
/// bubble shakes, see app/Bubble/OneEuroFilter.cs) doesn't throw the time away. Staying on another target for leaveMs
/// moves the timer to that one, counted from when the gaze got there.
/// A target is any number the caller picks, the wheel's options for one. UpdateSpot is for a resting gaze instead: the
/// target is a circle around where it came to rest. The action-wheel callers currently provide their durations from
/// internal WheelSettings, see app/Settings/AppSettings.cs
/// </summary>
public sealed class GazeDwell(int dwellMs, int leaveMs)
{
    // the one target UpdateSpot uses
    private const int SpotTarget = 0;

    private long _startMs;
    // where the gaze went instead (null target: nowhere) and since when, null while it's on Target
    private (int? Target, long SinceMs)? _away;
    private bool _picked;
    private (double X, double Y)? _anchor;

    public int DwellMs { get; set; } = dwellMs;
    public int LeaveMs { get; set; } = leaveMs;

    /// <summary>What the timer is running for, null for nothing.</summary>
    public int? Target { get; private set; }

    /// <summary>How far the timer is, 0-1.</summary>
    public double Progress { get; private set; }

    /// <summary>Where UpdateSpot's gaze came to rest, null before it has.</summary>
    public (double X, double Y)? Anchor => _anchor;

    public void Reset()
    {
        Target = null;
        _away = null;
        _picked = false;
        _anchor = null;
        Progress = 0;
    }

    /// <summary>target: what the gaze is on now, null for nothing. True once, on the frame the time is up while the gaze
    /// is on the target.</summary>
    public bool Update(long ms, int? target)
    {
        if (target == Target)
        {
            _away = null;
        }
        else if (Target is null)
        {
            Start(target, ms);
        }
        else
        {
            if (_away is not { } away || away.Target != target || away.SinceMs > ms) _away = away = (target, ms);
            if (ms - away.SinceMs >= LeaveMs)
            {
                Reset();
                Start(target, away.SinceMs);
            }
        }

        if (Target is null)
        {
            Progress = 0;
            return false;
        }
        Progress = Math.Clamp((ms - _startMs) / (double)DwellMs, 0, 1);
        if (Progress < 1 || _picked || target != Target) return false;
        _picked = true;
        return true;
    }

    /// <summary>The gaze resting on one spot: within radius of where it came to rest (same units as x, y). Wandering
    /// further for LeaveMs starts over where it is then.</summary>
    public bool UpdateSpot(long ms, double x, double y, double radius)
    {
        if (_anchor is { } a && Math.Sqrt((x - a.X) * (x - a.X) + (y - a.Y) * (y - a.Y)) > radius)
        {
            Update(ms, null);
            if (Target is not null) return false;
        }
        _anchor ??= (x, y);
        return Update(ms, SpotTarget);
    }

    private void Start(int? target, long ms)
    {
        Target = target;
        _startMs = ms;
        _away = null;
        _picked = false;
    }
}
