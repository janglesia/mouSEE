namespace app.Calibration;

// The accuracy test (AccuracyTestWindow): points all over the screen, looked at one by one while the tracker runs with
// the calibration in use, and where the bubble actually landed for each. More points than the calibration, and some
// while leaning, so it shows how good the calibration is on the rest of the screen and when you move. Saved to
// app/Calibration/Saved/tests/ to work out what to improve. See docs/SAVED-CALIBRATIONS.md

/// <summary>One point of the test. Phase: how to sit for it.</summary>
public sealed record TestPoint(string Phase, double X, double Y);

/// <summary>Where the bubble landed for one point.</summary>
public sealed record TestPointResult
{
    public string Phase { get; init; } = "";
    public double X { get; init; }
    public double Y { get; init; }
    public int Frames { get; init; }
    // frames where the tracker had a bubble position
    public int Estimates { get; init; }
    public Spread? EstimateX { get; init; }
    public Spread? EstimateY { get; init; }
    // median bubble position - the point, + is too far right / down. null without estimates
    public double? ErrorX { get; init; }
    public double? ErrorY { get; init; }
    public Spread? FaceX { get; init; }
}

public sealed record AccuracyTestReport
{
    public DateTimeOffset MadeAt { get; init; }
    // the calibration that was in use, null for the default values
    public string? CalibrationId { get; init; }
    public string? CalibrationName { get; init; }
    public double ScreenWidth { get; init; }
    public double ScreenHeight { get; init; }
    // the re-center's shift that was in use (already taken off the bubble positions in Points and the frames)
    public double OffsetX { get; init; }
    public double OffsetY { get; init; }
    public List<TestPointResult> Points { get; init; } = [];
    // per phase: average left/right error and which way it leans (share of the screen width)
    public Dictionary<string, double> MeanErrorX { get; init; } = [];
    public Dictionary<string, double> BiasX { get; init; } = [];
    public List<string> Summary { get; init; } = [];
}

/// <summary>What gets saved: the report and every frame, so the fit can be tried again on it.</summary>
public sealed record AccuracyTestFile(AccuracyTestReport Report, List<TargetRecording> Frames);

public static class AccuracyTest
{
    public const string Normal = "Sit normally";
    public const string LeanLeft = "Lean left";
    public const string LeanRight = "Lean right";

    public const int SettleMs = 800;   // the eyes land on the point, not recorded
    public const int CaptureMs = 1500;

    public static string Say(string phase) => phase switch
    {
        LeanLeft => "Now lean to the left, and stay there. Look at each dot.",
        LeanRight => "Now lean to the right, and stay there. Look at each dot.",
        _ => "Sit how you normally would, and look at each dot until the next one shows up.",
    };

    /// <summary>
    /// 5 columns by 3 rows sitting normally, more columns than the calibration has, so in between points get tested too.
    /// Then left, middle and right while leaning each way. Snake order, so the dot never jumps back across the screen.
    /// </summary>
    public static IReadOnlyList<TestPoint> Points { get; } = BuildPoints();

    private static List<TestPoint> BuildPoints()
    {
        double[] xs = [0.05, 0.275, 0.5, 0.725, 0.95];
        double[] ys = [0.15, 0.5, 0.85];
        var list = new List<TestPoint>();
        for (int r = 0; r < ys.Length; r++)
            for (int c = 0; c < xs.Length; c++)
                list.Add(new TestPoint(Normal, r % 2 == 0 ? xs[c] : xs[^(c + 1)], ys[r]));
        foreach (var phase in new[] { LeanLeft, LeanRight })
            foreach (double x in new[] { 0.1, 0.5, 0.9 })
                list.Add(new TestPoint(phase, x, 0.5));
        return list;
    }

    public static TestPointResult Result(TestPoint point, IReadOnlyList<FrameSample> frames)
    {
        var withEstimate = frames.Where(f => f.EstimateX is not null).ToList();
        var ex = Spread.Of(withEstimate.Select(f => f.EstimateX));
        var ey = Spread.Of(withEstimate.Select(f => f.EstimateY));
        return new TestPointResult
        {
            Phase = point.Phase,
            X = point.X,
            Y = point.Y,
            Frames = frames.Count,
            Estimates = withEstimate.Count,
            EstimateX = ex,
            EstimateY = ey,
            ErrorX = ex is null ? null : Math.Round(ex.Median - point.X, 3),
            ErrorY = ey is null ? null : Math.Round(ey.Median - point.Y, 3),
            FaceX = Spread.Of(frames.Where(f => f.FaceFound).Select(f => (double?)f.FaceX)),
        };
    }

    public static AccuracyTestReport Report(IReadOnlyList<TestPointResult> points, CalibrationProfile? calibration,
                                            double screenWidth, double screenHeight)
    {
        var mean = new Dictionary<string, double>();
        var bias = new Dictionary<string, double>();
        var summary = new List<string>();
        string Px(double fraction) => screenWidth > 0 ? $" ({fraction * screenWidth:0} px)" : "";

        foreach (var phase in new[] { Normal, LeanLeft, LeanRight })
        {
            var errors = points.Where(p => p.Phase == phase && p.ErrorX is not null).Select(p => p.ErrorX!.Value).ToList();
            if (errors.Count == 0) continue;
            mean[phase] = Math.Round(errors.Average(Math.Abs), 3);
            bias[phase] = Math.Round(errors.Average(), 3);
            string way = Math.Abs(bias[phase]) < 0.01 ? "no side in particular"
                       : bias[phase] > 0 ? $"on average {bias[phase]:P1} too far right" : $"on average {-bias[phase]:P1} too far left";
            summary.Add($"{phase}: the bubble was {mean[phase]:P1}{Px(mean[phase])} of the screen width off left/right, {way}.");
        }

        // where it's worst sitting normally: by column
        var columns = points.Where(p => p.Phase == Normal && p.ErrorX is not null).GroupBy(p => p.X).OrderBy(g => g.Key)
            .Select(g => $"{g.Key:0.##}: {g.Average(p => p.ErrorX!.Value):+0.000;-0.000}").ToList();
        if (columns.Count > 0) summary.Add("Sitting normally, average error per column (+ = too far right): " + string.Join(", ", columns));

        int missing = points.Count(p => p.Estimates == 0);
        if (missing > 0) summary.Add($"{missing} point(s) had no bubble at all (eyes closed, face lost, or no estimate).");

        return new AccuracyTestReport
        {
            MadeAt = DateTimeOffset.Now,
            CalibrationId = calibration?.Id,
            CalibrationName = calibration?.Name,
            ScreenWidth = screenWidth,
            ScreenHeight = screenHeight,
            Points = [.. points],
            MeanErrorX = mean,
            BiasX = bias,
            Summary = summary,
        };
    }
}
