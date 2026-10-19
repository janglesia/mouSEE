using app.Calibration;

namespace app.Tests;

public class GazeFitTests
{
    // the nine-point targets of the first real calibration (2026-10-18, 2560x1440): median horizontal iris position per eye
    private static readonly (double X, double Y, double L, double R)[] Real =
    [
        (0.08, 0.08, -0.435, -0.322), (0.50, 0.08, -0.133, 0.129), (0.92, 0.08, 0.291, 0.477),
        (0.92, 0.50, 0.278, 0.484), (0.50, 0.50, -0.144, 0.104), (0.08, 0.50, -0.441, -0.286),
        (0.08, 0.92, -0.374, -0.145), (0.50, 0.92, -0.148, 0.157), (0.92, 0.92, 0.160, 0.465),
    ];

    private static Spread At(double median) => new(median, median, median, median, median);

    private static List<GazeTargetSummary> Targets(IEnumerable<(double X, double Y, double L, double R)> points, int usable = 70) =>
        points.Select((p, i) => new GazeTargetSummary
        {
            Index = i, X = p.X, Y = p.Y, Frames = usable, UsableFrames = usable, LeftH = At(p.L), RightH = At(p.R),
        }).ToList();

    [Fact]
    public void Real_calibration_fits_to_a_few_percent()
    {
        var (model, report) = GazeFit.FitX(Targets(Real), 2560)!.Value;

        // the default numbers overshot a lot (the bubble was always further out than where you looked)
        Assert.True(report.DefaultMeanError > 0.4, $"default {report.DefaultMeanError}");
        Assert.True(report.MeanError < 0.05, $"fitted {report.MeanError}");
        Assert.True(report.LeaveOneOutError < 0.06, $"leave-one-out {report.LeaveOneOutError}");
        Assert.Equal(9, report.Targets.Count);
        Assert.Contains("px", report.Explanation);

        // a more linear mapping fits these eyes better than the default k of 0.7
        Assert.Equal(3.0, model.KH);
        Assert.Equal(-0.1417, model.LeftHBaseline!.Value, 4);
        Assert.Equal(0.13, model.RightHBaseline!.Value, 4);
        Assert.Equal(0.4852, model.CenterX!.Value, 3);
        Assert.Equal(3.5464, model.GainX!.Value, 3);
        // up/down isn't fitted, those stay at the tracker's defaults
        Assert.Null(model.KV);
        Assert.Null(model.GainY);
    }

    [Fact]
    public void Predictions_match_the_tracker()
    {
        // what tracker/gaze_model.py estimate_gaze() gives with these numbers (worked out with the tracker itself).
        // if the fit and the tracker do different math, the bubble won't land where the fit says
        double[] tracker = [0.0411, 0.4898, 0.95, 0.9464, 0.4685, 0.0593, 0.1843, 0.4974, 0.8637];
        var (_, report) = GazeFit.FitX(Targets(Real), 2560)!.Value;
        for (int i = 0; i < tracker.Length; i++)
            Assert.Equal(tracker[i], report.Targets[i].PredictedX, 2);
    }

    [Fact]
    public void Perfectly_linear_eyes_fit_exactly()
    {
        var points = new[] { 0.08, 0.5, 0.92 }.SelectMany(x => new[] { 0.08, 0.5, 0.92 }
            .Select(y => (x, y, L: -0.1 + (x - 0.5) * 0.9, R: 0.1 + (x - 0.5) * 0.9)));
        var (model, report) = GazeFit.FitX(Targets(points), 1920)!.Value;
        Assert.True(report.MaxError < 0.01, $"{report.MaxError}");
        Assert.Equal(-0.1, model.LeftHBaseline!.Value, 3);
        Assert.Equal(0.5, model.CenterX!.Value, 2);
    }

    [Fact]
    public void Validation_and_badly_tracked_targets_are_left_out()
    {
        var targets = Targets(Real);
        targets.Add(new GazeTargetSummary { Index = 9, X = 0.25, Y = 0.25, IsValidation = true, UsableFrames = 70, LeftH = At(5), RightH = At(5) });
        targets.Add(new GazeTargetSummary { Index = 10, X = 0.75, Y = 0.25, UsableFrames = 3, LeftH = At(5), RightH = At(5) });

        var (_, report) = GazeFit.FitX(targets, 2560)!.Value;

        Assert.Equal(9, report.Targets.Count);
        Assert.True(report.MeanError < 0.05);
    }

    [Fact]
    public void Too_few_targets_dont_fit()
    {
        Assert.Null(GazeFit.FitX(Targets(Real.Take(2)), 2560));
        // three targets all in one column can't say anything about left/right
        Assert.Null(GazeFit.FitX(Targets(Real.Where(p => p.X == 0.5)), 2560));
        Assert.Null(GazeFit.FitX([], 2560));
    }

    // frames of the lean target made with a known correction: looking at targetX while the face moves, the eyes turn
    // exactly so that the uncorrected model is off by -trueGain * (how far the face moved)
    private static List<FrameSample> Lean(GazeModel m, double targetX, double faceRef, double trueGain, double reach = 0.08,
                                          int count = 200)
    {
        return Enumerable.Range(0, count).Select(i =>
        {
            double moved = reach * Math.Sin(i * 0.1);
            double uncorrected = targetX - trueGain * moved;
            double angle = Math.Atan((uncorrected - m.CenterX!.Value) / m.GainX!.Value);
            double l = m.LeftHBaseline!.Value + m.KH!.Value * Math.Sin(angle);
            double r = m.RightHBaseline!.Value + m.KH!.Value * Math.Sin(angle);
            var eye = (double h) => new EyeSample(0.3, 0.5, 0.05, 0.33, 60, false, false, h, 0);
            return new FrameSample(i * 33, true, 2, -15, eye(l), eye(r), 30, 0, faceRef + moved, 0.4, 0.3);
        }).ToList();
    }

    [Fact]
    public void Leaning_correction_is_fitted_from_the_lean_target()
    {
        var (model, report) = GazeFit.FitX(Targets(Real), 2560)!.Value;

        var (withHead, headReport) = GazeFit.FitHeadX(model, report, Lean(model, 0.5, 0.47, trueGain: 1.6), 0.5, 0.47);

        Assert.True(headReport.HeadFitted, headReport.HeadExplanation);
        Assert.Equal(1.6, withHead.HeadXGain!.Value, 2);
        Assert.Equal(0.47, withHead.HeadXRef);
        Assert.True(headReport.LeanErrorBefore > 0.05);
        Assert.True(headReport.LeanErrorAfter < 0.005);
        Assert.Contains("corrected", headReport.HeadExplanation);
        // the rest of the fit stays the same
        Assert.Equal(model with { HeadXGain = null, HeadXRef = null }, withHead with { HeadXGain = null, HeadXRef = null });
    }

    [Fact]
    public void Not_leaning_enough_isnt_fitted()
    {
        var (model, report) = GazeFit.FitX(Targets(Real), 2560)!.Value;
        var (same, headReport) = GazeFit.FitHeadX(model, report, Lean(model, 0.5, 0.47, 1.6, reach: 0.005), 0.5, 0.47);

        Assert.False(headReport.HeadFitted);
        Assert.Null(same.HeadXGain);
        Assert.Contains("Lean further", headReport.HeadExplanation);
    }

    [Fact]
    public void A_correction_too_big_to_be_real_isnt_used()
    {
        var (model, report) = GazeFit.FitX(Targets(Real), 2560)!.Value;
        var (same, headReport) = GazeFit.FitHeadX(model, report, Lean(model, 0.5, 0.47, trueGain: 20, reach: 0.04), 0.5, 0.47);
        Assert.False(headReport.HeadFitted);
        Assert.Null(same.HeadXGain);
    }

    [Fact]
    public void Fit_does_both_and_measures_leaning_from_where_the_face_was()
    {
        var targets = Targets(Real).Select(t => t with { FaceX = At(0.47) }).ToList();
        var lean = Lean(GazeFit.FitX(targets, 2560)!.Value.Model, 0.5, 0.47, trueGain: 1.2);
        // the lean target's summary is in the list too, it mustn't count as one of the nine
        targets.Add(new GazeTargetSummary { Index = 13, X = 0.5, Y = 0.5, MoveHead = true, UsableFrames = 200, LeftH = At(0.4), RightH = At(0.4) });

        var (model, report) = GazeFit.Fit(targets, 2560, lean, 0.5)!.Value;

        Assert.Equal(9, report.Targets.Count);
        Assert.Equal(1.2, model.HeadXGain!.Value, 2);
        Assert.Equal(0.47, model.HeadXRef);
    }

    [Fact]
    public void Fit_without_a_lean_target_is_just_left_right()
    {
        var (model, report) = GazeFit.Fit(Targets(Real), 2560)!.Value;
        Assert.Null(model.HeadXGain);
        Assert.False(report.HeadFitted);
    }

    [Fact]
    public void Frame_knows_where_the_face_is()
    {
        var msg = new TrackerMessage("frame", 1,
            [new Face([0.3, 0.2, 0.4, 0.5], [], new Irises([0, 0], [0, 0]), new Pose(0, 0, 0), [])], null, 0, 0);
        var f = FrameSample.From(msg, 0);
        Assert.Equal(0.5, f.FaceX, 6);
        Assert.Equal(0.45, f.FaceY, 6);
        Assert.Equal(0.4, f.FaceW, 6);
    }

    // the nine targets of a real calibration (2026-10-18 23:00): row, lid openness and vertical iris offset (both eyes
    // averaged), and each eye's vertical offset
    private static readonly (double Y, double Open, double V, double LeftV, double RightV)[] RealVertical =
    [
        (0.08, 0.3579, -0.0323, -0.0235, -0.0423), (0.08, 0.3361, -0.0665, -0.0700, -0.0617), (0.08, 0.3370, -0.0631, -0.0773, -0.0491),
        (0.50, 0.3111, -0.0552, -0.0622, -0.0481), (0.50, 0.3058, -0.0536, -0.0556, -0.0519), (0.50, 0.3236, -0.0177, -0.0126, -0.0226),
        (0.92, 0.2799, -0.0160, -0.0129, -0.0188), (0.92, 0.2627, -0.0321, -0.0294, -0.0346), (0.92, 0.2591, -0.0409, -0.0366, -0.0448),
    ];

    private static List<GazeTargetSummary> VerticalTargets(IEnumerable<(double Y, double Open, double V, double LeftV, double RightV)> rows) =>
        rows.Select((r, i) => new GazeTargetSummary
        {
            Index = i, X = 0.5, Y = r.Y, Frames = 70, UsableFrames = 70,
            Openness = At(r.Open), OffsetV = At(r.V), LeftV = At(r.LeftV), RightV = At(r.RightV),
        }).ToList();

    [Fact]
    public void Up_down_is_fitted_from_the_lids()
    {
        var (model, report) = GazeFit.FitY(new GazeModel(), new GazeFitReport(), VerticalTargets(RealVertical));

        Assert.True(report.YFitted, report.YExplanation);
        // about what numpy's least squares gives on the full recording (these inputs are rounded to 4 decimals)
        Assert.Equal(3.5225, model.YConst!.Value, tolerance: 0.01);
        Assert.Equal(-9.068, model.YOpenness!.Value, tolerance: 0.02);   // more open = higher on the screen (smaller y)
        Assert.Equal(5.4409, model.YIris!.Value, tolerance: 0.05);
        Assert.True(report.YMeanError < 0.12, $"{report.YMeanError}");
        Assert.True(report.YDefaultMeanError > report.YMeanError);
        Assert.Equal(9, report.YTargets.Count);
        Assert.Contains("lids", report.YExplanation);
    }

    [Fact]
    public void Up_down_needs_targets_in_two_rows()
    {
        var oneRow = VerticalTargets(RealVertical.Where(r => r.Y == 0.5).Concat(RealVertical.Where(r => r.Y == 0.5)));
        var (model, report) = GazeFit.FitY(new GazeModel(), new GazeFitReport(), oneRow);
        Assert.False(report.YFitted);
        Assert.Null(model.YOpenness);
    }

    [Fact]
    public void Up_down_needs_lid_openness()
    {
        // calibrations from before openness was in the summary: up/down stays at the defaults
        var (model, report) = GazeFit.FitY(new GazeModel(), new GazeFitReport(), Targets(Real));
        Assert.False(report.YFitted);
        Assert.Null(model.YConst);
        Assert.Contains("default", report.YExplanation);
    }

    [Fact]
    public void Fit_does_up_down_too_and_says_which_version()
    {
        var targets = Targets(Real).Zip(RealVertical, (t, v) => t with { Y = v.Y, Openness = At(v.Open), OffsetV = At(v.V) }).ToList();
        var (model, report) = GazeFit.Fit(targets, 2560)!.Value;

        Assert.True(report.YFitted);
        Assert.NotNull(model.YOpenness);
        Assert.NotNull(model.GainX);
        Assert.Equal(GazeFit.Version, report.Version);
    }

    [Fact]
    public void Target_summary_keeps_lid_openness_and_vertical_offset()
    {
        var eye = (double open, double v) => new EyeSample(0.3, 0.5, 0.05, open, 60, false, false, 0, v);
        var frames = Enumerable.Range(0, 20).Select(i => new FrameSample(i * 33, true, 0, 0, eye(0.30, -0.02), eye(0.34, -0.04), 30)).ToList();
        var s = GazeTargetSummary.Of(new CalibrationTarget(0, 0.5, 0.08), frames);
        Assert.Equal(0.32, s.Openness!.Median, 6);
        Assert.Equal(-0.03, s.OffsetV!.Median, 6);
    }

    [Fact]
    public void Missing_eye_data_is_skipped()
    {
        var targets = Targets(Real);
        targets[0] = targets[0] with { RightH = null };
        Assert.Equal(8, GazeFit.FitX(targets, 2560)!.Value.Report.Targets.Count);
    }
}
