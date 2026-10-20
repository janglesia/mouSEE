namespace app.Calibration.Accuracy;

// The accuracy test (AccuracyTestWindow): points all over the screen, looked at one by one while the tracker runs with
// the calibration in use, and where the bubble actually landed for each. More points than the calibration, and some
// while leaning, so it shows how good the calibration is on the rest of the screen and when you move. Saved to
// app/Calibration/Saved/tests/ to work out what to improve. See docs/calibration/ACCURACY-TEST.md

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
    // the head mode that was in use (docs/bubble/HEAD-MODES.md), to compare them. the test measures the eyes, not the nudge:
    // in the nudge mode head movement isn't cancelled, so the leaning and tilting parts show how much it moves the bubble
    public HeadMode HeadMode { get; init; }
    public List<TestPointResult> Points { get; init; } = [];
    // per phase: average left/right error and which way it leans (share of the screen width)
    public Dictionary<string, double> MeanErrorX { get; init; } = [];
    public Dictionary<string, double> BiasX { get; init; } = [];
    // per phase: average up/down error (share of the screen height)
    public Dictionary<string, double> MeanErrorY { get; init; } = [];
    public List<string> Summary { get; init; } = [];
}

/// <summary>What gets saved: the report and every frame, so the fit can be tried again on it.</summary>
public sealed record AccuracyTestFile(AccuracyTestReport Report, List<TargetRecording> Frames);

public static class AccuracyTest
{
    public const string Normal = "Sit normally";
    public const string LeanLeft = "Lean left";
    public const string LeanRight = "Lean right";
    public const string HeadUp = "Head tilted up";
    public const string HeadDown = "Head tilted down";

    // every phase in order, and which HeadPose guides it (how far to lean or tilt). null sitting normally
    public static readonly string[] Phases = [Normal, LeanLeft, LeanRight, HeadUp, HeadDown];
    public static string? Pose(string phase) => phase switch
    {
        LeanLeft => HeadPose.LeanLeft,
        LeanRight => HeadPose.LeanRight,
        HeadUp => HeadPose.HeadUp,
        HeadDown => HeadPose.HeadDown,
        _ => null,
    };

    public const int SettleMs = 800;   // the eyes land on the point, not recorded
    public const int CaptureMs = 1500;

    public static string Say(string phase) => phase switch
    {
        LeanLeft => "Now lean to the left, and stay there. Look at each dot.",
        LeanRight => "Now lean to the right, and stay there. Look at each dot.",
        HeadUp => "Sit normally again, and tilt your head up a little. Keep it there and look at each dot.",
        HeadDown => "Now tilt your head down a little. Keep it there and look at each dot.",
        _ => "Sit how you normally would, and look at each dot until the next one shows up.",
    };

    /// <summary>
    /// 5 columns by 4 rows sitting normally, further out than the grid's columns and between its rows, so in between
    /// points get tested too. 4 rows because these are also the fine-tune's dots, and with 3 its up/down curves had too
    /// little to go on. Then left, middle and right while leaning each way, and top, middle and bottom with the head
    /// tilted up and down (how far is guided, HeadPose.Guide). Snake order, so the dot never jumps back across the screen.
    /// </summary>
    public static IReadOnlyList<TestPoint> Points { get; } = BuildPoints();

    private static List<TestPoint> BuildPoints()
    {
        double[] xs = [0.05, 0.275, 0.5, 0.725, 0.95];
        double[] ys = [0.15, 0.38, 0.62, 0.85];
        var list = new List<TestPoint>();
        for (int r = 0; r < ys.Length; r++)
            for (int c = 0; c < xs.Length; c++)
                list.Add(new TestPoint(Normal, r % 2 == 0 ? xs[c] : xs[^(c + 1)], ys[r]));
        foreach (var phase in new[] { LeanLeft, LeanRight })
            foreach (double x in new[] { 0.1, 0.5, 0.9 })
                list.Add(new TestPoint(phase, x, 0.5));
        foreach (double y in new[] { 0.15, 0.5, 0.85 }) list.Add(new TestPoint(HeadUp, 0.5, y));
        foreach (double y in new[] { 0.85, 0.5, 0.15 }) list.Add(new TestPoint(HeadDown, 0.5, y));
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
        var meanY = new Dictionary<string, double>();
        var summary = new List<string>();
        string Px(double fraction) => screenWidth > 0 ? $" ({fraction * screenWidth:0} px)" : "";

        foreach (var phase in Phases)
        {
            var errors = points.Where(p => p.Phase == phase && p.ErrorX is not null).Select(p => p.ErrorX!.Value).ToList();
            if (errors.Count == 0) continue;
            mean[phase] = Math.Round(errors.Average(Math.Abs), 3);
            bias[phase] = Math.Round(errors.Average(), 3);
            string way = Math.Abs(bias[phase]) < 0.01 ? "no side in particular"
                       : bias[phase] > 0 ? $"on average {bias[phase]:P1} too far right" : $"on average {-bias[phase]:P1} too far left";
            var errorsY = points.Where(p => p.Phase == phase && p.ErrorY is not null).Select(p => Math.Abs(p.ErrorY!.Value)).ToList();
            if (errorsY.Count > 0) meanY[phase] = Math.Round(errorsY.Average(), 3);
            string upDown = errorsY.Count > 0 ? $", up/down {meanY[phase]:P1} of the screen height" : "";
            summary.Add($"{phase}: the bubble was {mean[phase]:P1}{Px(mean[phase])} of the screen width off left/right, {way}{upDown}.");
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
            MeanErrorY = meanY,
            Summary = summary,
        };
    }
}
