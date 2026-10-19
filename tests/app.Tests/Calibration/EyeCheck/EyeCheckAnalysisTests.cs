using System.IO;
using System.Text.RegularExpressions;

namespace app.Tests;

public class EyeCheckAnalysisTests
{
    [Fact]
    public void Good_run_tunes_everything_between_open_and_closed()
    {
        var outcome = EyeCheckAnalysis.Analyze(FakeEyeCheck.GoodRun());
        var t = outcome.Thresholds;

        // open iris up to ~0.32, closed from ~0.92
        Assert.InRange(t.IrisHiddenRatio!.Value, 0.55, 0.7);
        // not tuned, it would need a hand over the eye
        Assert.Null(t.CoveredContrast);
        Assert.Equal(-10, t.RestPitch);
        Assert.Equal(2, t.RestYaw);
        Assert.True(outcome.TunedDetection);
        Assert.Empty(outcome.Warnings);
    }

    [Fact]
    public void Open_lids_are_tuned_between_open_and_closed()
    {
        // open eyes 0.35, closed 0.2
        var t = EyeCheckAnalysis.Analyze(FakeEyeCheck.GoodRun()).Thresholds;
        Assert.InRange(t.LidsOpenOpenness!.Value, 0.22, 0.33);
    }

    [Fact]
    public void Glasses_reflections_still_tune_the_lids()
    {
        // what happened with glasses: with the screen reflecting on them the open iris ratio overlaps the closed one,
        // so IRIS_HIDDEN_RATIO can't be tuned. The lids aren't affected and still separate open from closed
        var run = FakeEyeCheck.GoodRun();
        run[EyeCheckStepKind.LookAround] = FakeEyeCheck.Frames(200, i => (FakeEyeCheck.Open(i) with { IrisRatio = 0.6 + i % 10 * 0.03 },
                                                                 FakeEyeCheck.Open(i) with { IrisRatio = 0.6 + i % 10 * 0.03 }));
        var outcome = EyeCheckAnalysis.Analyze(run);

        Assert.Null(outcome.Thresholds.IrisHiddenRatio);
        Assert.NotNull(outcome.Thresholds.LidsOpenOpenness);
        Assert.True(outcome.TunedDetection); // so the check round runs
    }

    [Fact]
    public void Lids_too_close_keep_the_default()
    {
        var run = FakeEyeCheck.GoodRun();
        // looking down brings the lid down to about where a closed eye is
        run[EyeCheckStepKind.LookAround] = FakeEyeCheck.Frames(200, i => (FakeEyeCheck.Open(i) with { Openness = i % 4 == 0 ? 0.21 : 0.35 },
                                                                 FakeEyeCheck.Open(i)));
        var outcome = EyeCheckAnalysis.Analyze(run);

        Assert.Null(outcome.Thresholds.LidsOpenOpenness);
        var lids = outcome.Values.Single(v => v.Name == "LIDS_OPEN_OPENNESS");
        Assert.False(lids.Tuned);
        Assert.Equal(EyeCheckAnalysis.DefaultLidsOpenOpenness, lids.Used);
        Assert.Contains("0.21", lids.Explanation); // says what was measured
        Assert.Equal(0.21, lids.Measured["openLowest"]);
    }

    [Fact]
    public void Values_are_clamped()
    {
        // closed eyes so obvious the middle would be above 0.85
        var run = FakeEyeCheck.GoodRun();
        run[EyeCheckStepKind.LookAround] = FakeEyeCheck.Frames(200, i => (FakeEyeCheck.Open(i) with { IrisRatio = 0.8 }, FakeEyeCheck.Open(i) with { IrisRatio = 0.8 }));
        foreach (var kind in new[] { EyeCheckStepKind.CloseLeft, EyeCheckStepKind.CloseRight, EyeCheckStepKind.CloseLeftHeadUp,
                                     EyeCheckStepKind.CloseRightHeadDown, EyeCheckStepKind.CloseBoth })
            run[kind] = FakeEyeCheck.Frames(100, i => (FakeEyeCheck.Closed(i) with { IrisRatio = 1.2 }, FakeEyeCheck.Closed(i) with { IrisRatio = 1.2 }));

        Assert.Equal(EyeCheckAnalysis.IrisMax, EyeCheckAnalysis.Analyze(run).Thresholds.IrisHiddenRatio);
    }

    [Fact]
    public void Open_and_closed_too_alike_keeps_the_default()
    {
        var run = FakeEyeCheck.GoodRun();
        run[EyeCheckStepKind.LookAround] = FakeEyeCheck.Frames(200, i => (FakeEyeCheck.Open(i) with { IrisRatio = 0.85 }, FakeEyeCheck.Open(i) with { IrisRatio = 0.85 }));

        var outcome = EyeCheckAnalysis.Analyze(run);

        Assert.Null(outcome.Thresholds.IrisHiddenRatio);
        var iris = outcome.Values.Single(v => v.Name == "IRIS_HIDDEN_RATIO");
        Assert.False(iris.Tuned);
        Assert.Contains("glasses", iris.Explanation);
        Assert.True(iris.Measured["gap"] < EyeCheckAnalysis.MinIrisGap);
        Assert.NotNull(outcome.Thresholds.RestPitch); // the head angle is still fine
    }

    [Fact]
    public void Missing_steps_keep_the_defaults()
    {
        var run = FakeEyeCheck.GoodRun();
        foreach (var kind in new[] { EyeCheckStepKind.CloseLeft, EyeCheckStepKind.CloseRight, EyeCheckStepKind.CloseLeftHeadUp,
                                     EyeCheckStepKind.CloseRightHeadDown, EyeCheckStepKind.CloseBoth })
            run.Remove(kind);

        var outcome = EyeCheckAnalysis.Analyze(run);

        Assert.Null(outcome.Thresholds.IrisHiddenRatio);
        Assert.NotNull(outcome.Thresholds.RestPitch);
        Assert.False(outcome.TunedDetection);
        Assert.Contains("Not measured", outcome.Values.Single(v => v.Name == "IRIS_HIDDEN_RATIO").Explanation);
    }

    [Fact]
    public void Nothing_recorded_changes_nothing()
    {
        var outcome = EyeCheckAnalysis.Analyze(new Dictionary<EyeCheckStepKind, IReadOnlyList<FrameSample>>());
        Assert.Equal(new EyeThresholds(), outcome.Thresholds);
        Assert.False(outcome.TunedDetection);
    }

    [Fact]
    public void Head_limits_stay_at_the_defaults_for_small_head_movements()
    {
        var t = EyeCheckAnalysis.Analyze(FakeEyeCheck.GoodRun()).Thresholds;
        Assert.Equal(EyeCheckAnalysis.DefaultMaxPitch, t.MaxPitch);
        Assert.Equal(EyeCheckAnalysis.DefaultMaxYaw, t.MaxYaw);
    }

    [Fact]
    public void Head_limits_grow_when_looking_at_the_corners_turns_the_head_a_lot()
    {
        // turning 30° left and right to look at the corners, the default 35° would leave only 5° to spare
        var run = FakeEyeCheck.GoodRun();
        run[EyeCheckStepKind.LookAround] = FakeEyeCheck.Frames(200, i => (FakeEyeCheck.Open(i), FakeEyeCheck.Open(i)), yawAt: i => 2 + (i % 2 == 0 ? 30 : -30));

        var t = EyeCheckAnalysis.Analyze(run).Thresholds;

        Assert.Equal(40, t.MaxYaw);
        Assert.Equal(EyeCheckAnalysis.DefaultMaxPitch, t.MaxPitch);
    }

    [Fact]
    public void Warns_about_small_eyes_and_a_slow_camera()
    {
        var run = FakeEyeCheck.GoodRun();
        run[EyeCheckStepKind.Rest] = FakeEyeCheck.Frames(90, i => (FakeEyeCheck.Open(i) with { WidthPx = 20 }, FakeEyeCheck.Open(i) with { WidthPx = 20 }), fps: 15);

        var warnings = EyeCheckAnalysis.Analyze(run).Warnings;

        Assert.Contains(warnings, w => w.Contains("20 px wide") && w.Contains("Move closer"));
        Assert.Contains(warnings, w => w.Contains("15 frames a second"));
    }

    [Fact]
    public void Defaults_match_the_tracker()
    {
        // the analysis compares against the tracker's defaults, so they must not drift apart
        string extractor = File.ReadAllText(RepoFile("tracker/eye_extractor.py"));
        string status = File.ReadAllText(RepoFile("tracker/tracking_status.py"));
        Assert.Equal(EyeCheckAnalysis.DefaultIrisHiddenRatio, Constant(extractor, "IRIS_HIDDEN_RATIO"));
        Assert.Equal(EyeCheckAnalysis.DefaultLidsOpenOpenness, Constant(extractor, "LIDS_OPEN_OPENNESS"));
        Assert.Equal(EyeCheckAnalysis.DefaultCoveredContrast, Constant(extractor, "COVERED_CONTRAST"));
        Assert.Equal(EyeCheckAnalysis.DefaultRestPitch, Constant(status, "REST_PITCH"));
        Assert.Equal(EyeCheckAnalysis.DefaultRestYaw, Constant(status, "REST_YAW"));
        Assert.Equal(EyeCheckAnalysis.DefaultMaxPitch, Constant(status, "MAX_PITCH"));
        Assert.Equal(EyeCheckAnalysis.DefaultMaxYaw, Constant(status, "MAX_YAW"));
    }

    [Fact]
    public void Looking_down_rule_matches_the_tracker()
    {
        string extractor = File.ReadAllText(RepoFile("tracker/eye_extractor.py"));
        Assert.Equal(GazeTargetSummary.SameLidsRatio, Constant(extractor, "SAME_LIDS_RATIO"));
        Assert.Equal(GazeTargetSummary.LookingDownMinOpenness, Constant(extractor, "LOOKING_DOWN_MIN_OPENNESS"));
        Assert.Equal(GazeTargetSummary.LookingDownMaxBlink, Constant(extractor, "LOOKING_DOWN_MAX_BLINK"));
    }

    [Fact]
    public void Looking_down_frames_count_even_when_recorded_as_closed()
    {
        // numbers from a real bottom-row frame: both lids 0.26, both "closed", blink 0.25
        EyeSample Eye(double open, double blink, bool closed) => new(0.75, 0.4, blink, open, 60, closed, false);
        FrameSample Frame(EyeSample l, EyeSample r) => new(0, true, 0, -15, l, r, 30);

        Assert.True(GazeTargetSummary.Usable(Frame(Eye(0.259, 0.24, true), Eye(0.257, 0.26, true))));
        // a wink: one lid far lower than the other
        Assert.False(GazeTargetSummary.Usable(Frame(Eye(0.17, 0.3, true), Eye(0.31, 0.05, false))));
        // both closed: lids nearly shut, high blink score
        Assert.False(GazeTargetSummary.Usable(Frame(Eye(0.04, 0.73, true), Eye(0.045, 0.73, true))));
    }

    private static double Constant(string python, string name) =>
        double.Parse(Regex.Match(python, $@"^{name}\s*=\s*([0-9.]+)", RegexOptions.Multiline).Groups[1].Value,
                     System.Globalization.CultureInfo.InvariantCulture);

    private static string RepoFile(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string path = Path.Combine(dir.FullName, relative);
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException(relative);
    }
}
