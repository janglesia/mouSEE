namespace app.Tests;

public class SqueezeAndHeadFitTests
{
    private static Spread At(double median) => new(median, median, median, median, median);

    // where the eyes point for x: angle from tan, then each eye's iris position with the model's k and baselines
    private static (double L, double R) Irises(GazeModel m, double tan)
    {
        double angle = Math.Atan(tan);
        return (m.LeftHBaseline!.Value + m.KH!.Value * Math.Sin(angle), m.RightHBaseline!.Value + m.KH!.Value * Math.Sin(angle));
    }

    // grid targets made with a known squeeze: looking down (less open lids) the iris moves less from side to side
    private static List<GazeTargetSummary> SqueezedNine(double squeeze)
    {
        var model = new GazeModel { KH = 3, LeftHBaseline = -0.12, RightHBaseline = 0.15 };
        double[] rowsY = [0.08, 0.5, 0.92];
        double[] open = [0.36, 0.32, 0.27];
        var list = new List<GazeTargetSummary>();
        int i = 0;
        for (int r = 0; r < 3; r++)
            foreach (double x in new[] { 0.08, 0.5, 0.92 })
            {
                // x = 0.49 + 3.6 * t + squeeze * t * (open - 0.32)  ->  t
                double t = (x - 0.49) / (3.6 + squeeze * (open[r] - 0.32));
                var (l, rr) = Irises(model, t);
                list.Add(new GazeTargetSummary
                {
                    Index = i++, X = x, Y = rowsY[r], Frames = 70, UsableFrames = 70,
                    LeftH = At(l), RightH = At(rr), Openness = At(open[r]), OffsetV = At(-0.04),
                    FaceX = At(0.5), FaceY = At(0.4), FaceW = 0.21, Pitch = At(-15),
                });
            }
        return list;
    }

    [Fact]
    public void Looking_down_squeeze_is_fitted_from_the_lids()
    {
        var (model, report) = GazeFit.FitX(SqueezedNine(-12), 2560)!.Value;

        Assert.NotNull(model.XSqueeze);
        Assert.Equal(-12, model.XSqueeze!.Value, 0);
        Assert.Equal(0.32, model.OpennessRef!.Value, 3);
        Assert.True(report.MeanError < 0.005, $"{report.MeanError}");
        Assert.Contains("looking down", report.Explanation);
    }

    [Fact]
    public void No_squeeze_without_lid_openness()
    {
        var targets = SqueezedNine(-12).Select(t => t with { Openness = null }).ToList();
        var (model, _) = GazeFit.FitX(targets, 2560)!.Value;
        Assert.Null(model.XSqueeze);
    }

    // the head step's dots, made from a known model and known head corrections
    private static List<GazeTargetSummary> HeadDots(GazeModel m, double gain, double shrink, double yPitch, double yLean,
                                                    double faceRef = 0.5, double pitchRef = -15, double lean = 0.15, double tilt = 8)
    {
        var dots = new List<GazeTargetSummary>();
        int i = 100;
        foreach (var (pose, x, y) in HeadPose.Step)
        {
            double moved = pose == HeadPose.LeanLeft ? -lean : pose == HeadPose.LeanRight ? lean : 0;
            double tilted = pose == HeadPose.HeadUp ? -tilt : pose == HeadPose.HeadDown ? tilt : 0;
            // x = center + gain_x t + gain * moved + shrink * t * |moved|  ->  t
            double t = (x - m.CenterX!.Value - gain * moved) / (m.GainX!.Value + shrink * Math.Abs(moved));
            var (l, r) = Irises(m, t);
            // y = yconst + yopen * open + yiris * v + yPitch * tilted + yLean * |moved|, with v = 0  ->  open
            double open = (y - m.YConst!.Value - yPitch * tilted - yLean * Math.Abs(moved)) / m.YOpenness!.Value;
            dots.Add(new GazeTargetSummary
            {
                Index = i++, X = x, Y = y, Pose = pose, Frames = 70, UsableFrames = 70,
                LeftH = At(l), RightH = At(r), Openness = At(open), OffsetV = At(0),
                FaceX = At(faceRef + moved), FaceY = At(0.4), FaceW = 0.21, Pitch = At(pitchRef + tilted),
            });
        }
        return dots;
    }

    private static readonly GazeModel Base = new()
    {
        KH = 3, LeftHBaseline = -0.12, RightHBaseline = 0.15, CenterX = 0.49, GainX = 3.6,
        YConst = 3.5, YOpenness = -9.0, YIris = 0,
    };

    [Fact]
    public void Leaning_and_tilting_are_fitted_from_the_head_step()
    {
        var dots = HeadDots(Base, gain: 0.5, shrink: -6, yPitch: 0.02, yLean: 0.8);

        var (model, report) = GazeFit.FitHead(Base, new GazeFitReport(), dots, 0.5, -15);

        Assert.True(report.HeadFitted, report.HeadExplanation);
        Assert.Equal(0.5, model.HeadXGain!.Value, 2);
        Assert.Equal(-6, model.HeadXShrink!.Value, 1);
        Assert.Equal(0.02, model.HeadYPitch!.Value, 3);
        Assert.Equal(0.8, model.HeadYLean!.Value, 2);
        Assert.Equal(-15, model.PitchRef);
        Assert.True(report.LeanErrorAfter < 0.005);
        Assert.True(report.HeadYErrorAfter < 0.005);
        Assert.True(report.LeanErrorBefore > report.LeanErrorAfter);
    }

    [Fact]
    public void Not_tilting_enough_leaves_the_tilt_out()
    {
        var dots = HeadDots(Base, gain: 0.5, shrink: -6, yPitch: 0.02, yLean: 0.8, tilt: 1);
        var (model, report) = GazeFit.FitHead(Base, new GazeFitReport(), dots, 0.5, -15);
        Assert.Null(model.HeadYPitch);
        Assert.NotNull(model.HeadXGain);
        Assert.Contains("tilt a bit more", report.HeadExplanation);
    }

    [Fact]
    public void Not_leaning_enough_leaves_leaning_out()
    {
        var dots = HeadDots(Base, gain: 0.5, shrink: -6, yPitch: 0.02, yLean: 0, lean: 0.01);
        var (model, report) = GazeFit.FitHead(Base, new GazeFitReport(), dots, 0.5, -15);
        Assert.Null(model.HeadXGain);
        Assert.Null(model.HeadXShrink);
        Assert.Contains("lean further", report.HeadExplanation);
    }

    [Fact]
    public void Fit_uses_the_head_step_when_there_is_one()
    {
        var nine = SqueezedNine(0);
        var first = GazeFit.Fit(nine, 2560)!.Value.Model;
        var withY = first with { YConst = 3.5, YOpenness = -9.0, YIris = 0 };
        var all = nine.Concat(HeadDots(withY, gain: 0.5, shrink: 0, yPitch: 0, yLean: 0, faceRef: 0.5)).ToList();

        var (model, report) = GazeFit.Fit(all, 2560)!.Value;

        Assert.Equal(GazeFit.Version, report.Version);
        Assert.Equal(9, report.Targets.Count);                  // the head dots aren't part of the left/right fit
        Assert.Equal(0.5, model.HeadXGain!.Value, 1);
        Assert.NotEmpty(report.HeadTargets);
    }
}
