namespace app.Tests;

public class OneEuroFilterTests
{
    private const double Frame = 1 / 60.0;

    // the gaze resting on one spot: shaking around it by about 1% of the screen, like the bubble does
    private static List<(double X, double Y)> Resting(int frames, double x = 0.5, double y = 0.5, int seed = 1)
    {
        var random = new Random(seed);
        double Noise() => (random.NextDouble() - 0.5) * 0.04;   // +-2%, about 1.2% standard deviation
        return Enumerable.Range(0, frames).Select(_ => (x + Noise(), y + Noise())).ToList();
    }

    private static double Spread(IEnumerable<double> values)
    {
        var list = values.ToList();
        double mean = list.Average();
        return Math.Sqrt(list.Average(v => (v - mean) * (v - mean)));
    }

    private static List<(double X, double Y)> Run(GazeSmoothing smoothing, IEnumerable<(double X, double Y)> input, double start = 0) =>
        input.Select((p, i) => smoothing.Filter(p.X, p.Y, start + i * Frame)).ToList();

    [Fact]
    public void Resting_gaze_shakes_much_less()
    {
        var input = Resting(300);
        // after the first second, once it's settled
        var output = Run(new GazeSmoothing(0.5), input).Skip(60).ToList();
        double before = Spread(input.Skip(60).Select(p => p.X)), after = Spread(output.Select(p => p.X));
        Assert.True(after < before * 0.3, $"shake {before:0.0000} -> {after:0.0000}");
        // and it stays where the eyes are
        Assert.Equal(0.5, output.Average(p => p.X), 2);
    }

    [Fact]
    public void A_jump_across_the_screen_is_followed_quickly()
    {
        // resting on the left, then the eyes jump to the right
        var smoothing = new GazeSmoothing(0.5);
        Run(smoothing, Resting(60, x: 0.2));
        var after = Run(smoothing, Resting(30, x: 0.8, seed: 2), start: 60 * Frame);
        // within 100 ms (6 frames) it's most of the way there, within 200 ms there
        Assert.True(after[5].X > 0.7, $"after 100 ms: {after[5].X:0.000}");
        Assert.InRange(after[11].X, 0.75, 0.85);
    }

    [Fact]
    public void Sensitivity_trades_steadiness_for_speed()
    {
        var input = Resting(300);
        double steady = Spread(Run(new GazeSmoothing(0), input).Skip(60).Select(p => p.X));
        double quick = Spread(Run(new GazeSmoothing(1), input).Skip(60).Select(p => p.X));
        Assert.True(steady < quick, $"{steady} vs {quick}");
        Assert.Equal(GazeSmoothing.SteadiestCutoff, GazeSmoothing.Cutoff(0), 6);
        Assert.Equal(GazeSmoothing.QuickestCutoff, GazeSmoothing.Cutoff(1), 6);
        Assert.Equal(Math.Sqrt(0.3 * 3.0), GazeSmoothing.Cutoff(0.5), 6);
        // out of range is clamped
        Assert.Equal(GazeSmoothing.QuickestCutoff, GazeSmoothing.Cutoff(5), 6);
    }

    [Fact]
    public void Changing_the_sensitivity_takes_effect_right_away()
    {
        var input = Resting(300);
        var smoothing = new GazeSmoothing(1) { Sensitivity = 0 };
        double changed = Spread(Run(smoothing, input).Skip(60).Select(p => p.X));
        double steady = Spread(Run(new GazeSmoothing(0), input).Skip(60).Select(p => p.X));
        Assert.Equal(steady, changed, 6);
    }

    [Fact]
    public void After_a_gap_it_starts_over_instead_of_sliding_in()
    {
        var smoothing = new GazeSmoothing(0.5);
        Run(smoothing, Resting(60, x: 0.2));
        // eyes closed for half a second, then open looking somewhere else: the bubble shows up there straight away
        var (x, _) = smoothing.Filter(0.8, 0.5, 60 * Frame + 0.5);
        Assert.Equal(0.8, x, 6);
    }

    [Fact]
    public void Steady_input_comes_out_unchanged()
    {
        var filter = new OneEuroFilter(1, 1.5);
        for (int i = 0; i < 100; i++) Assert.Equal(0.42, filter.Filter(0.42, i * Frame), 9);
    }

    [Fact]
    public void Same_or_earlier_time_doesnt_break_it()
    {
        var filter = new OneEuroFilter(1, 1.5);
        filter.Filter(0.3, 1.0);
        double v = filter.Filter(0.9, 1.0);   // two frames with the same timestamp
        Assert.True(double.IsFinite(v));
        Assert.Equal(0.3, v, 9);
    }
}
