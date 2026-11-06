namespace app.Bubble;

/// <summary>
/// The 1 euro filter (Casiez, Roussel, Vogel, CHI 2012): a low-pass filter whose cutoff goes up with speed. Holding
/// still it filters hard (the shake of a gaze resting on one spot goes away), moving fast it hardly filters (a jump to
/// another part of the screen isn't dragged out). minCutoff: the cutoff (Hz) at rest, lower is steadier. beta: how much
/// the cutoff rises per unit of speed (units per second), higher follows fast moves sooner. One value, see GazeSmoothing
/// for both directions of the bubble.
/// </summary>
public sealed class OneEuroFilter(double minCutoff, double beta, double derivativeCutoff = 1.0)
{
    private double? _x, _dx;
    private double _lastSeconds;

    public double MinCutoff { get; set; } = minCutoff;
    public double Beta { get; set; } = beta;

    public void Reset() => (_x, _dx) = (null, null);

    /// <summary>The filtered value for a new one at this time (seconds, going up).</summary>
    public double Filter(double value, double seconds)
    {
        if (_x is not { } last || seconds <= _lastSeconds)
        {
            // the first value, or a time that didn't move on: nothing to filter against yet
            _x ??= value;
            _dx ??= 0;
            _lastSeconds = Math.Max(_lastSeconds, seconds);
            return _x.Value;
        }
        double dt = seconds - _lastSeconds;
        _lastSeconds = seconds;
        double dx = Smooth(_dx!.Value, (value - last) / dt, Alpha(derivativeCutoff, dt));
        _dx = dx;
        double cutoff = MinCutoff + Beta * Math.Abs(dx);
        _x = Smooth(last, value, Alpha(cutoff, dt));
        return _x.Value;
    }

    private static double Smooth(double previous, double value, double alpha) => previous + alpha * (value - previous);

    // how far to move towards the new value, for a low-pass filter with this cutoff and time step
    private static double Alpha(double cutoff, double dt)
    {
        double tau = 1 / (2 * Math.PI * cutoff);
        return 1 / (1 + tau / dt);
    }
}

/// <summary>
/// Smooths the bubble (x, y as 0-1 of the screen) with a 1 euro filter per direction. The cursor.sensitivity setting
/// picks how steady against how quick: 0 holds very still but follows a little later, 1 follows at once but shakes more.
/// Starts over after a gap (the bubble was hidden: eyes closed, face lost, paused), so it doesn't slide in from where it
/// was. See docs/bubble/SMOOTHING.md
/// </summary>
public sealed class GazeSmoothing
{
    // the cutoff at rest goes from this (sensitivity 0) to this (1), evenly on a log scale: 0.5 is about 1 Hz
    public const double SteadiestCutoff = 0.3, QuickestCutoff = 3.0;
    // per unit of speed (share of the screen per second): a jump across half the screen is about 10/s, which lifts the
    // cutoff to ~15 Hz, so it lags by a frame or so. resting, the gaze's shake moves well under 0.1/s
    public const double Beta = 1.5;
    // a gap longer than this and it starts over
    public const double ResetAfterSeconds = 0.3;

    private readonly OneEuroFilter _x, _y;
    private double? _lastSeconds;

    public GazeSmoothing(double sensitivity = 0.5)
    {
        double cutoff = Cutoff(sensitivity);
        _x = new OneEuroFilter(cutoff, Beta);
        _y = new OneEuroFilter(cutoff, Beta);
    }

    /// <summary>The cutoff at rest (Hz) for a sensitivity of 0-1.</summary>
    public static double Cutoff(double sensitivity) =>
        SteadiestCutoff * Math.Pow(QuickestCutoff / SteadiestCutoff, Math.Clamp(sensitivity, 0, 1));

    public double Sensitivity
    {
        set => _x.MinCutoff = _y.MinCutoff = Cutoff(value);
    }

    public void Reset()
    {
        _x.Reset();
        _y.Reset();
        _lastSeconds = null;
    }

    public (double X, double Y) Filter(double x, double y, double seconds)
    {
        if (_lastSeconds is { } last && seconds - last > ResetAfterSeconds) Reset();
        _lastSeconds = seconds;
        return (_x.Filter(x, seconds), _y.Filter(y, seconds));
    }
}
