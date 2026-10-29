using System.IO;
using System.Text.RegularExpressions;

namespace app.Tests;

public class EyeCheckAnalysisTests
{
    [Fact]
    public void Good_run_tunes_the_shut_lids_between_open_and_closed()
    {
        // open eyes from 0.2, shut ones up to 0.048: halfway is 0.124
        var outcome = EyeCheckAnalysis.Analyze(FakeEyeCheck.Grid(openness: i => 0.2 + i % 4 * 0.05), FakeEyeCheck.GoodRun());
        var t = outcome.Thresholds;

        Assert.Equal(0.124, t.LidsShutOpenness!.Value, 3);
        Assert.True(outcome.TunedDetection);
        Assert.Empty(outcome.Warnings);
        // the wink values and covered aren't tuned
        Assert.Null(t.IrisHiddenRatio);
        Assert.Null(t.LidsOpenOpenness);
        Assert.Null(t.CoveredContrast);
    }

    [Fact]
    public void Shut_lids_stay_below_looking_down()
    {
        // open eyes far apart from shut ones: halfway would be above 0.15, where looking down stops counting as open
        var t = EyeCheckAnalysis.Analyze(FakeEyeCheck.Grid(openness: _ => 0.35), FakeEyeCheck.GoodRun()).Thresholds;
        Assert.Equal(EyeCheckAnalysis.ShutMax, t.LidsShutOpenness);
    }

    [Fact]
    public void Resting_head_angle_comes_from_the_grid()
    {
        var grid = FakeEyeCheck.Frames(300, i => (FakeEyeCheck.Open(i), FakeEyeCheck.Open(i)), pitch: -13.5, yaw: 2.4);
        var t = EyeCheckAnalysis.Analyze(grid, FakeEyeCheck.GoodRun()).Thresholds;
        Assert.Equal(-13.5, t.RestPitch);
        Assert.Equal(2.4, t.RestYaw);
        // the head turn limits stay at the tracker's defaults
        Assert.Null(t.MaxPitch);
        Assert.Null(t.MaxYaw);
    }

    [Fact]
    public void Blinks_during_the_grid_arent_open_eyes()
    {
        // every 20th grid frame a blink: lids shut like the closed steps. left out, so the open eyes still start at 0.26
        var grid = FakeEyeCheck.Frames(400, i => i % 20 == 0
            ? (FakeEyeCheck.Closed(i), FakeEyeCheck.Closed(i))
            : (FakeEyeCheck.Open(i) with { Openness = 0.26 }, FakeEyeCheck.Open(i) with { Openness = 0.26 }));
        var outcome = EyeCheckAnalysis.Analyze(grid, FakeEyeCheck.GoodRun());

        Assert.NotNull(outcome.Thresholds.LidsShutOpenness);
        Assert.Equal(0.26, outcome.Values.Single(v => v.Name == "LIDS_SHUT_OPENNESS").Measured["openLowest"]);
    }

    [Fact]
    public void Eyes_not_closed_all_the_way_keep_the_default()
    {
        // the blink score said closed, but the lids only came down to where the open eyes looking down are
        var run = FakeEyeCheck.GoodRun();
        foreach (var kind in run.Keys.ToList())
            run[kind] = FakeEyeCheck.Frames(90, i => (FakeEyeCheck.Closed(i) with { Openness = 0.24 }, FakeEyeCheck.Closed(i) with { Openness = 0.24 }));
        var outcome = EyeCheckAnalysis.Analyze(FakeEyeCheck.Grid(), run);

        Assert.Null(outcome.Thresholds.LidsShutOpenness);
        Assert.False(outcome.TunedDetection);
        var lids = outcome.Values.Single(v => v.Name == "LIDS_SHUT_OPENNESS");
        Assert.False(lids.Tuned);
        Assert.Equal(EyeCheckAnalysis.DefaultLidsShutOpenness, lids.Used);
        Assert.Contains("0.24", lids.Explanation); // says what was measured
        Assert.True(lids.Measured["gap"] < EyeCheckAnalysis.MinShutGap);
        Assert.NotNull(outcome.Thresholds.RestPitch); // the head angle is still fine
    }

    [Fact]
    public void Missing_steps_keep_the_default()
    {
        var outcome = EyeCheckAnalysis.Analyze(FakeEyeCheck.Grid(), new Dictionary<EyeCheckStepKind, IReadOnlyList<FrameSample>>());

        Assert.Null(outcome.Thresholds.LidsShutOpenness);
        Assert.NotNull(outcome.Thresholds.RestPitch);
        Assert.False(outcome.TunedDetection);
        Assert.Contains("Not measured", outcome.Values.Single(v => v.Name == "LIDS_SHUT_OPENNESS").Explanation);
    }

    [Fact]
    public void Nothing_recorded_changes_nothing()
    {
        var outcome = EyeCheckAnalysis.Analyze([], new Dictionary<EyeCheckStepKind, IReadOnlyList<FrameSample>>());
        Assert.Equal(new EyeThresholds(), outcome.Thresholds);
        Assert.False(outcome.TunedDetection);
    }

    [Fact]
    public void Warns_about_small_eyes_and_a_slow_camera()
    {
        var grid = FakeEyeCheck.Frames(300, i => (FakeEyeCheck.Open(i) with { WidthPx = 20 }, FakeEyeCheck.Open(i) with { WidthPx = 20 }), fps: 15);

        var warnings = EyeCheckAnalysis.Analyze(grid, FakeEyeCheck.GoodRun()).Warnings;

        Assert.Contains(warnings, w => w.Contains("20 px wide") && w.Contains("Move closer"));
        Assert.Contains(warnings, w => w.Contains("15 frames a second"));
    }

    [Fact]
    public void Defaults_match_the_tracker()
    {
        // the analysis compares against the tracker's defaults, so they must not drift apart
        string extractor = File.ReadAllText(RepoFile("tracker/eye_extractor.py"));
        string status = File.ReadAllText(RepoFile("tracker/tracking_status.py"));
        Assert.Equal(EyeCheckAnalysis.DefaultLidsShutOpenness, Constant(extractor, "LIDS_SHUT_OPENNESS"));
        Assert.Equal(EyeCheckAnalysis.DefaultIrisHiddenRatio, Constant(extractor, "IRIS_HIDDEN_RATIO"));
        Assert.Equal(EyeCheckAnalysis.DefaultLidsOpenOpenness, Constant(extractor, "LIDS_OPEN_OPENNESS"));
        Assert.Equal(EyeCheckAnalysis.DefaultCoveredContrast, Constant(extractor, "COVERED_CONTRAST"));
        Assert.Equal(EyeCheckAnalysis.DefaultRestPitch, Constant(status, "REST_PITCH"));
        Assert.Equal(EyeCheckAnalysis.DefaultRestYaw, Constant(status, "REST_YAW"));
    }

    [Fact]
    public void Shut_lids_limit_matches_looking_down()
    {
        // above LOOKING_DOWN_MIN_OPENNESS the looking down rule would undo a closed eye
        Assert.Equal(GazeTargetSummary.LookingDownMinOpenness, EyeCheckAnalysis.ShutMax);
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
