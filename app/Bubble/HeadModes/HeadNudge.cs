namespace app.Bubble.HeadModes;

/// <summary>
/// Head nudge, for the nudge and both modes (HeadMode.cs): the eyes get the bubble close, the head does the last bit.
///
/// Following: the bubble is where the eyes are. Once they stay on one spot for the settle time (head.nudgeSettleMs), the
/// bubble holds there, and the head angle at that moment counts as zero. Holding: turning your head moves the bubble
/// left/right, tilting it moves it up/down, head.nudgeStrength % of the screen per degree past the dead zone. Looking
/// somewhere else (the eyes further than head.nudgeReleasePercent from both the held spot and the nudged bubble, for a
/// moment) lets go, and the bubble follows the eyes again. Both, because looking at the yellow bubble after nudging it
/// is looking away from the held spot, and that used to let go.
///
/// Measured from the moment it holds, not from how you sat during calibration, so posture doesn't push the bubble.
/// From the first recordings: sitting still the head angles move 0.1-0.2° from frame to frame, looking around the whole
/// screen turns the head less than 1° and tilts it about 2°, leaning or tilting on purpose is 6-11°. So a nudge is easy to
/// tell from looking around. Yaw goes up turning to your right, pitch goes up tilting down (both from the tracker),
/// which is right and down on the screen.
/// </summary>
public sealed class HeadNudge(HeadSettings settings)
{
    // how much of each frame's head angle goes into the smoothed one. at 60 fps it's about 50 ms behind
    public const double HeadSmoothing = 0.3;
    // the eyes are settled when nearly all positions over the settle time are this close to their middle
    public const double SettleRadius = 0.06;
    public const double SettledShare = 0.9;
    // looking away has to last this long to let go, so one bad frame doesn't
    public const int ReleaseMs = 150;
    // the furthest a nudge goes from the held spot, either way
    public const double MaxNudge = 0.35;

    private sealed class Hold(double x, double y, double yaw, double pitch)
    {
        public double X { get; } = x;
        public double Y { get; } = y;
        public double Yaw { get; } = yaw;
        public double Pitch { get; } = pitch;
        public long? AwaySince { get; set; }
    }

    private readonly Queue<(long Ms, double X, double Y)> _recent = new();
    private (double Yaw, double Pitch)? _head;
    private Hold? _hold;

    public HeadSettings Settings { get; set; } = settings;

    /// <summary>true while the bubble holds on a spot and the head nudges it.</summary>
    public bool Holding => _hold is not null;

    /// <summary>How far the head moves it from the held spot right now (share of the screen), 0 while following.</summary>
    public (double X, double Y) Nudge { get; private set; }

    /// <summary>Where the bubble goes this frame. x, y: where the eyes are (after the fine-tune and re-center),
    /// yaw, pitch: the head angle from the tracker, ms: any clock that goes up.</summary>
    public (double X, double Y) Update(long ms, double x, double y, double yaw, double pitch)
    {
        _head = _head is { } h ? (h.Yaw + (yaw - h.Yaw) * HeadSmoothing, h.Pitch + (pitch - h.Pitch) * HeadSmoothing) : (yaw, pitch);
        var head = _head.Value;

        if (_hold is { } hold)
        {
            // where the bubble is now, held spot plus the nudge so far. the eyes looking there is still holding
            var (bx, by) = (hold.X + Nudge.X, hold.Y + Nudge.Y);
            double release = Settings.NudgeReleasePercent / 100.0;
            if (Distance(x, y, hold.X, hold.Y) > release && Distance(x, y, bx, by) > release)
            {
                hold.AwaySince ??= ms;
                if (ms - hold.AwaySince >= ReleaseMs) _hold = null;
            }
            else
            {
                hold.AwaySince = null;
            }

            if (_hold is not null)
            {
                Nudge = (Axis(head.Yaw - hold.Yaw), Axis(head.Pitch - hold.Pitch));
                return (hold.X + Nudge.X, hold.Y + Nudge.Y);
            }
        }

        Nudge = (0, 0);
        _recent.Enqueue((ms, x, y));
        while (ms - _recent.Peek().Ms > Settings.NudgeSettleMs) _recent.Dequeue();
        if (Settled() is { } spot)
        {
            _hold = new Hold(spot.X, spot.Y, head.Yaw, head.Pitch);
            _recent.Clear();
            return spot;
        }
        return (x, y);
    }

    /// <summary>Back to following, and forgets the head angle (after a pause, or when the mode changes).</summary>
    public void Reset()
    {
        _recent.Clear();
        _head = null;
        _hold = null;
        Nudge = (0, 0);
    }

    // the middle of the last settle time's positions, if they cover it and nearly all are close to it
    private (double X, double Y)? Settled()
    {
        if (_recent.Count < 3 || _recent.Last().Ms - _recent.Peek().Ms < Settings.NudgeSettleMs * 0.8) return null;
        double mx = Stats.Percentile(_recent.Select(p => p.X).ToList(), 50);
        double my = Stats.Percentile(_recent.Select(p => p.Y).ToList(), 50);
        int close = _recent.Count(p => Distance(p.X, p.Y, mx, my) <= SettleRadius);
        return close >= _recent.Count * SettledShare ? (mx, my) : null;
    }

    // degrees past the dead zone, times the strength, no further than MaxNudge
    private double Axis(double degrees)
    {
        double past = Math.Max(0, Math.Abs(degrees) - Settings.NudgeDeadZone);
        return Math.Clamp(Math.Sign(degrees) * past * Settings.NudgeStrength / 100, -MaxNudge, MaxNudge);
    }

    private static double Distance(double x1, double y1, double x2, double y2) => Math.Sqrt((x1 - x2) * (x1 - x2) + (y1 - y2) * (y1 - y2));
}
