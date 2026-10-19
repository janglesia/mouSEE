namespace app.Tests;

// the 20-point grid and the curved up/down fit (GazeFit.FitY)
public class GridFitTests
{
    private static Spread At(double median) => new(median, median, median, median, median);

    private static readonly GazeModel LeftRight = new() { KH = 3, LeftHBaseline = -0.14, RightHBaseline = 0.13, CenterX = 0.5, GainX = 3.6 };

    // 5 columns by 4 rows like the calibration screen. the lids a bit less open each row down (0.36 at the top to 0.26 at
    // the bottom, like the recordings), and where each target lands made with y = 3.5 - 9 * openness + arc * tan² +
    // sq * openness², plus a bit of noise so leaving a target out has something to measure
    private static List<GazeTargetSummary> Grid(double arc, double sq = 0, double noise = 0.01)
    {
        double[] xs = [0.07, 0.285, 0.5, 0.715, 0.93], ys = [0.07, 0.3567, 0.6433, 0.93];
        var list = new List<GazeTargetSummary>();
        int i = 0;
        foreach (double y in ys)
            foreach (double x in xs)
            {
                double t = (x - 0.5) / 3.6;
                double sin = Math.Sin(Math.Atan(t));
                double o = 0.36 - 0.11 * y;
                double landed = 3.5 - 9 * o + arc * t * t + sq * o * o + noise * ((i * 7 % 5) - 2) / 2;
                list.Add(new GazeTargetSummary
                {
                    Index = i++, X = x, Y = landed, Frames = 70, UsableFrames = 70,
                    LeftH = At(-0.14 + 3 * sin), RightH = At(0.13 + 3 * sin),
                    Openness = At(o), OffsetV = At(0.01 * (i % 3)), LeftV = At(0), RightV = At(0),
                });
            }
        return list;
    }

    [Fact]
    public void Straight_up_down_stays_straight()
    {
        var (model, report) = GazeFit.FitY(LeftRight, new GazeFitReport(), Grid(arc: 0));
        Assert.True(report.YFitted, report.YExplanation);
        Assert.Equal("flat", report.YShape);
        Assert.Null(model.YArc);
        Assert.Null(model.YOpennessSq);
    }

    [Fact]
    public void The_sides_reading_higher_is_fitted_as_an_arc()
    {
        // the sides about 13% of the height higher than the middle, like the first fine-tunes found
        var (model, report) = GazeFit.FitY(LeftRight, new GazeFitReport(), Grid(arc: -9));

        Assert.NotEqual("flat", report.YShape);
        Assert.Equal(-9, model.YArc!.Value, tolerance: 1.5);
        Assert.Equal(-9.0, model.YOpenness!.Value, tolerance: 1.0);
        Assert.True(report.YLeaveOneOutError < 0.02, $"{report.YLeaveOneOutError}");
        Assert.Contains("sides", report.YExplanation);

        // a straight line can't do that
        var flat = GazeFit.FitY(LeftRight, new GazeFitReport(), Grid(arc: -9).Select(t => t with { LeftH = null, RightH = null }).ToList());
        Assert.Equal("flat", flat.Report.YShape);
        Assert.True(flat.Report.YLeaveOneOutError > report.YLeaveOneOutError * 2);
    }

    [Fact]
    public void Uneven_up_down_is_fitted_too()
    {
        // stretched more at the top: still one openness for every row (sq small enough that y keeps going one way)
        var (model, report) = GazeFit.FitY(LeftRight, new GazeFitReport(), Grid(arc: -9, sq: 60, noise: 0.002));
        Assert.Equal("arc-and-uneven", report.YShape);
        Assert.Equal(60, model.YOpennessSq!.Value, tolerance: 6);
    }

    [Fact]
    public void The_head_step_uses_the_curved_up_down()
    {
        // tilted dots that land where the curved up/down puts them need no tilt correction
        var grid = Grid(arc: -9);
        var (model, report) = GazeFit.FitY(LeftRight, new GazeFitReport(), grid);
        var dots = grid.Where(t => t.X is 0.07 or 0.93).Take(4).Select((t, i) => t with
        {
            Index = 100 + i, Pose = i % 2 == 0 ? HeadPose.HeadUp : HeadPose.HeadDown, FaceX = At(0.5), Pitch = At(i % 2 == 0 ? -20 : -4),
            Y = model.YConst!.Value + model.YOpenness!.Value * t.Openness!.Median + model.YIris!.Value * t.OffsetV!.Median
                + model.YArc!.Value * Math.Pow((t.X - 0.5) / 3.6, 2),
        }).ToList();
        var head = GazeFit.FitHead(model, report, dots, faceXRef: 0.5, pitchRef: -12);
        Assert.True(head.Report.HeadYErrorBefore < 0.005, $"{head.Report.HeadYErrorBefore}");
    }
}
