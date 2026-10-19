using System.Text.Json;

namespace app.Tests;

public class FineTuneTests
{
    // the bubble a known correction away from the dots, with a bit of noise so leave-one-out has something to measure
    private static List<FineTunePoint> Points(Func<double, double, (double X, double Y)> bubble, double noise = 0.004)
    {
        var random = new Random(7);
        return FineTune.Dots.Select(d =>
        {
            var (x, y) = bubble(d.X, d.Y);
            return new FineTunePoint(d.X, d.Y, x + (random.NextDouble() - 0.5) * noise, y + (random.NextDouble() - 0.5) * noise);
        }).ToList();
    }

    [Fact]
    public void Dots_AreTwentyInsideTheScreen()
    {
        Assert.Equal(20, FineTune.Dots.Count);
        Assert.All(FineTune.Dots, d => Assert.InRange(d.X, 0.05, 0.95));
        Assert.All(FineTune.Dots, d => Assert.InRange(d.Y, 0.05, 0.95));
        Assert.Equal(20, FineTune.Dots.Distinct().Count());
    }

    [Fact]
    public void Fit_UndoesAStretchAndShift()
    {
        // the bubble lands 8% too far right and with up/down squashed to 60% around the middle
        var points = Points((x, y) => (x + 0.08, 0.5 + (y - 0.5) * 0.6));
        var (c, report) = FineTune.Fit(points);

        Assert.True(report.CorrectsX);
        Assert.True(report.CorrectsY);
        foreach (var p in points)
        {
            var (x, y) = c.Apply(p.BubbleX, p.BubbleY);
            Assert.Equal(p.TargetX, x, 0.01);
            Assert.Equal(p.TargetY, y, 0.01);
        }
        Assert.True(report.ErrorYAfter < report.ErrorYBefore / 3);
        Assert.Equal(20, report.Dots.Count);
    }

    [Fact]
    public void Fit_UndoesTheArcAndTheWiderTop()
    {
        // what a camera under the screen did: the sides land higher than the middle, and left/right is stretched at
        // the top and squeezed at the bottom
        var points = Points((x, y) =>
        {
            double u = x - 0.5, v = y - 0.5;
            return (0.5 + u * (1 - 0.8 * v), y - 0.6 * u * u);
        });
        var (c, report) = FineTune.Fit(points);

        Assert.True(report.CorrectsX);
        Assert.True(report.CorrectsY);
        Assert.NotEqual("flat", report.ShapeX);
        Assert.NotEqual("flat", report.ShapeY);
        // the curves don't undo it exactly in the corners, but on average it's close
        Assert.InRange(points.Average(p => Math.Abs(c.Apply(p.BubbleX, p.BubbleY).X - p.TargetX)), 0, 0.02);
        Assert.InRange(points.Average(p => Math.Abs(c.Apply(p.BubbleX, p.BubbleY).Y - p.TargetY)), 0, 0.02);
        Assert.True(report.ErrorXAfter < report.ErrorXBefore / 2);
        Assert.True(report.ErrorYAfter < report.ErrorYBefore / 2);
    }

    [Fact]
    public void Fit_OnTheFirstRealFineTune_PicksTheCurves()
    {
        // the 12 dots of the first fine-tune (2026-10-19), camera under the screen tilted up
        (double, double, double, double)[] dots =
        [
            (0.12, 0.15, 0.085, -0.264), (0.37, 0.15, 0.359, -0.050), (0.63, 0.15, 0.678, -0.007), (0.88, 0.15, 0.959, -0.161),
            (0.88, 0.50, 0.945, 0.518), (0.63, 0.50, 0.642, 0.468), (0.37, 0.50, 0.393, 0.537), (0.12, 0.50, 0.090, 0.368),
            (0.12, 0.85, 0.235, 0.866), (0.37, 0.85, 0.432, 0.939), (0.63, 0.85, 0.620, 0.988), (0.88, 0.85, 0.817, 0.902),
        ];
        var (_, report) = FineTune.Fit(dots.Select(d => new FineTunePoint(d.Item1, d.Item2, d.Item3, d.Item4)).ToList());

        // a flat correction couldn't do anything for left/right (4.6%) and got up/down to 5.8%
        Assert.Equal("wider-at-one-end", report.ShapeX);
        Assert.Equal("arc-and-uneven", report.ShapeY);
        Assert.InRange(report.ErrorXAfter, 0, 0.042);
        Assert.InRange(report.ErrorYAfter, 0, 0.035);
    }

    [Fact]
    public void Correction_FromBeforeTheCurves_StillReads()
    {
        var old = JsonSerializer.Deserialize<GazeCorrection>("""{"x0":0,"x1":1,"x2":0,"y0":0.2632,"y1":-0.0772,"y2":0.6516}""",
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        Assert.Equal(new GazeCorrection(0, 1, 0, 0.2632, -0.0772, 0.6516), old);
    }

    [Fact]
    public void Fit_LeavesADirectionThatIsAlreadyRight()
    {
        // left/right is spot on, only up/down is off
        var (c, report) = FineTune.Fit(Points((x, y) => (x, y - 0.1)));

        Assert.False(report.CorrectsX);
        Assert.True(report.CorrectsY);
        Assert.Equal((0.0, 1.0, 0.0), (c.X0, c.X1, c.X2));
        Assert.Equal(0.5, c.Apply(0.5, 0.4).Y, 0.01);
    }

    [Fact]
    public void Fit_WithTooFewDots_ChangesNothing()
    {
        var (c, report) = FineTune.Fit(Points((x, y) => (x + 0.1, y)).Take(FineTune.MinPoints - 1).ToList());

        Assert.Equal(GazeCorrection.None, c);
        Assert.False(report.CorrectsX || report.CorrectsY);
        Assert.Contains("Not enough dots", report.Explanation);
    }

    [Fact]
    public void Fit_RefusesAHugeStretch()
    {
        // the bubble barely moves left/right: making that fit needs a stretch of 5, not a last adjustment
        var (c, report) = FineTune.Fit(Points((x, y) => (0.5 + (x - 0.5) * 0.2, y), noise: 0.0005));

        Assert.False(report.CorrectsX);
        Assert.Equal(1.0, c.X1);
    }

    [Fact]
    public void Bubble_IsTheFineTuneThenTheRecenter()
    {
        var c = new GazeCorrection(0.1, 1, 0, 0, 0, 2);
        var (x, y) = FineTune.Bubble(0.5, 0.25, c, new GazeOffset(0.05, 0.1));
        Assert.Equal(0.55, x, 6);
        Assert.Equal(0.4, y, 6);
        Assert.Equal((0.45, 0.15), FineTune.Bubble(0.5, 0.25, null, new GazeOffset(0.05, 0.1)));
    }

    private static FrameSample Frame(double? x, double? y) =>
        new(0, true, 0, 0, null, null, 30, EstimateX: x, EstimateY: y);

    [Fact]
    public void Point_IsTheMedianOfTheFramesWithABubble()
    {
        var frames = Enumerable.Range(0, 20)
            .Select(i => Frame(0.6 + (i == 0 ? 0.3 : 0), 0.4))
            .Append(Frame(null, null)).ToList();
        var point = FineTune.Point(0.5, 0.5, frames);

        Assert.NotNull(point);
        Assert.Equal(0.6, point!.BubbleX, 6);
        Assert.Equal(0.4, point.BubbleY, 6);
        Assert.Null(FineTune.Point(0.5, 0.5, frames.Take(10).ToList()));
    }

    [Fact]
    public void Profile_KeepsTheFineTune()
    {
        var profile = new CalibrationProfile { FineTune = new GazeCorrection(0.01, 1.1, 0, -0.02, 0, 1.3) };
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var back = JsonSerializer.Deserialize<CalibrationProfile>(JsonSerializer.Serialize(profile, options), options);
        Assert.Equal(profile.FineTune, back!.FineTune);
    }
}
