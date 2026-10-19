using app.Calibration;
using app.Gaze;

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

    // nine targets made with a known squeeze: looking down (less open lids) the iris moves less from side to side
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

public class RecenterTests
{
    private static FrameSample Bubble(double? x, double? y, double faceX = 0.5) =>
        new(0, true, 2, -14, null, null, 30, 0, faceX, 0.42, 0.21, x, y);

    [Fact]
    public void Offset_is_how_far_off_the_middle_the_bubble_was()
    {
        var frames = Enumerable.Range(0, 30).Select(i => Bubble(0.45 + (i % 3) * 0.001, 0.58)).ToList();
        var (offset, at, problem) = Recenter.From(frames);

        Assert.Null(problem);
        Assert.Equal(-0.049, offset!.X, 3);
        Assert.Equal(0.08, offset.Y, 3);
        Assert.Equal(0.5, at!.FaceX);
        Assert.Equal(-14, at.Pitch);
    }

    [Fact]
    public void Too_few_frames_with_a_bubble_is_not_used()
    {
        var frames = Enumerable.Range(0, 30).Select(i => i < 10 ? Bubble(0.5, 0.5) : Bubble(null, null)).ToList();
        var (offset, _, problem) = Recenter.From(frames);
        Assert.Null(offset);
        Assert.Contains("both eyes open", problem);
    }

    [Fact]
    public void Very_far_off_needs_a_new_calibration_not_a_re_center()
    {
        var (offset, _, problem) = Recenter.From(Enumerable.Range(0, 30).Select(_ => Bubble(0.9, 0.5)).ToList());
        Assert.Null(offset);
        Assert.Contains("new calibration", problem);
    }
}

public class PositionGuideTests
{
    private long _now;
    private static readonly HeadPosition Calibrated = new(0.5, 0.4, 0.21, -15);

    private static FrameSample At(double faceX, double faceY = 0.4, double faceW = 0.21, double pitch = -15) =>
        new(0, true, 0, pitch, null, null, 30, 0, faceX, faceY, faceW);

    private void Feed(PositionGuide g, FrameSample f, long ms)
    {
        for (long t = 0; t <= ms; t += 50)
        {
            g.OnFrame(f);
            _now += 50;
        }
    }

    private PositionGuide Make() => new(() => _now) { Calibrated = Calibrated };

    [Theory]
    [InlineData(0.65, 0.4, 0.21, "Move a little to your left")]       // face moved to the person's right in the image
    [InlineData(0.35, 0.4, 0.21, "Move a little to your right")]
    [InlineData(0.5, 0.55, 0.21, "Sit up a little")]                  // slid down
    [InlineData(0.5, 0.25, 0.21, "Sit a little lower")]
    [InlineData(0.5, 0.4, 0.27, "Move back a little")]                // face bigger: closer
    [InlineData(0.5, 0.4, 0.16, "Move a little closer")]
    public void Says_which_way_to_go(double x, double y, double w, string start)
    {
        Assert.StartsWith(start, PositionGuide.MoveFrom(Calibrated, new HeadPosition(x, y, w, -15)));
    }

    [Fact]
    public void Close_to_calibration_says_nothing_and_the_biggest_problem_wins()
    {
        Assert.Null(PositionGuide.MoveFrom(Calibrated, new HeadPosition(0.55, 0.43, 0.22, -12)));
        // 0.25 to the side is worse than slightly too close
        Assert.StartsWith("Move a little to your left", PositionGuide.MoveFrom(Calibrated, new HeadPosition(0.75, 0.4, 0.25, -15)));
    }

    [Fact]
    public void Move_hint_only_after_it_lasts_and_goes_away_after_moving_back()
    {
        var g = Make();
        Feed(g, At(0.7), 1000);
        Assert.Null(g.MoveHint);                         // not yet, could be passing through
        Feed(g, At(0.7), 700);
        Assert.StartsWith("Move a little to your left", g.MoveHint);

        Feed(g, At(0.59), 200);                          // still past 0.8 of the limit: stays
        Assert.NotNull(g.MoveHint);
        Feed(g, At(0.55), 200);
        Assert.Null(g.MoveHint);
    }

    [Fact]
    public void Suggests_a_re_center_after_moving_a_bit_for_a_while()
    {
        var g = Make();
        Feed(g, At(0.57), 3000);
        Assert.False(g.SuggestRecenter);
        Feed(g, At(0.57), 1500);
        Assert.True(g.SuggestRecenter);

        // re-centering there takes it away, and it doesn't come back staying there
        g.Recentered(new HeadPosition(0.57, 0.4, 0.21, -15));
        Assert.False(g.SuggestRecenter);
        Feed(g, At(0.57), 6000);
        Assert.False(g.SuggestRecenter);
    }

    [Fact]
    public void Head_tilt_alone_suggests_a_re_center()
    {
        var g = Make();
        Feed(g, At(0.5, pitch: -9), 4500);
        Assert.True(g.SuggestRecenter);
    }

    [Fact]
    public void Nothing_without_a_calibration_or_a_face()
    {
        var g = new PositionGuide(() => _now);
        Feed(g, At(0.9), 5000);
        Assert.Null(g.MoveHint);

        g.Calibrated = Calibrated;
        Feed(g, new FrameSample(0, false, 0, 0, null, null, 30), 5000);
        Assert.Null(g.MoveHint);
    }

    [Fact]
    public void Calibrated_position_comes_from_the_nine_targets()
    {
        var targets = Enumerable.Range(0, 9).Select(i => new GazeTargetSummary
        {
            Index = i, FaceX = new Spread(0, 0, 0.5 + i * 0.001, 0, 0), FaceY = new Spread(0, 0, 0.4, 0, 0), FaceW = 0.21,
            Pitch = new Spread(0, 0, -15, 0, 0),
        }).Append(new GazeTargetSummary { Index = 20, Pose = HeadPose.LeanLeft, FaceX = new Spread(0, 0, 0.3, 0, 0) }).ToList();

        var at = HeadPosition.Of(new CalibrationProfile { GazeTargets = targets })!;

        Assert.Equal(0.504, at.FaceX, 6);   // the lean dot isn't counted
        Assert.Equal(0.4, at.FaceY);
        Assert.Equal(-15, at.Pitch);
        Assert.Null(HeadPosition.Of(new CalibrationProfile()));
    }
}
