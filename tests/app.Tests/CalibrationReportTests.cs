using System.IO;
using System.Text.Json;
using app.Calibration;
using app.Gaze;

namespace app.Tests;

file static class F
{
    public static EyeSample Open(double h = 0, double v = 0, double openness = 0.35) =>
        new(0.25, 0.55, 0.05, openness, 60, false, false, h, v);

    public static FrameSample Frame(long ms, EyeSample? l, EyeSample? r, double pitch = -10, double yaw = 2, bool face = true) =>
        new(ms, face, yaw, pitch, l, r, 30, 1);

    public static List<FrameSample> Many(int n, Func<int, FrameSample> make) => Enumerable.Range(0, n).Select(make).ToList();
}

public class ValueReportTests
{
    private static List<EyeValueReport> GoodValues()
    {
        // reuse the eye check tests' good run
        var run = new Dictionary<EyeCheckStepKind, IReadOnlyList<FrameSample>>();
        foreach (var step in EyeCheckSteps.All)
        {
            bool l = step.ClosedEyes.Contains(Side.Left), r = step.ClosedEyes.Contains(Side.Right);
            EyeSample Eye(bool closed, int i) => closed
                ? new(0.92 + i % 5 * 0.01, 0.4, step.Kind == EyeCheckStepKind.CloseBoth ? 0.7 : 0.3, 0.2, 60, true, false)
                : new(0.25 + i % 10 * 0.008, 0.55, 0.05, 0.35, 60, false, false);
            run[step.Kind] = F.Many(100, i => F.Frame(1200 + i * 33, Eye(l, i), Eye(r, i),
                pitch: step.Kind == EyeCheckStepKind.LookAround ? -10 + i % 20 - 10 : -10));
        }
        return EyeCheckAnalysis.Analyze(run).Values;
    }

    [Fact]
    public void Every_value_is_reported_once_with_its_default()
    {
        var values = GoodValues();
        Assert.Equal(["REST_PITCH", "REST_YAW", "MAX_PITCH", "MAX_YAW", "IRIS_HIDDEN_RATIO", "LIDS_OPEN_OPENNESS", "COVERED_CONTRAST"],
                     values.Select(v => v.Name));
        Assert.All(values, v =>
        {
            Assert.NotNull(v.Default);
            Assert.False(string.IsNullOrWhiteSpace(v.Label));
            Assert.False(string.IsNullOrWhiteSpace(v.Explanation));
        });
    }

    [Fact]
    public void Default_head_limit_says_how_far_the_head_moved_and_what_was_needed()
    {
        var pitch = GoodValues().Single(v => v.Name == "MAX_PITCH");

        Assert.False(pitch.Tuned);
        Assert.Equal(30, pitch.Used);
        // head went ±10° in the fake run
        Assert.Equal(10, pitch.Measured["turnedFurthest"], 0);
        Assert.Contains("the default", pitch.Explanation);
        Assert.Contains("moved up to", pitch.Explanation);
        Assert.DoesNotContain("defaults are enough", pitch.Explanation);
    }

    [Fact]
    public void Tuned_value_says_what_it_replaced_and_why()
    {
        var iris = GoodValues().Single(v => v.Name == "IRIS_HIDDEN_RATIO");

        Assert.True(iris.Tuned);
        Assert.Contains("instead of the default 0.65", iris.Explanation);
        Assert.Contains("halfway between", iris.Explanation);
        Assert.True(iris.Measured["closedLowest"] > iris.Used && iris.Used > iris.Measured["openHighest"]);
    }

    [Fact]
    public void Covered_is_reported_as_not_measured()
    {
        var covered = GoodValues().Single(v => v.Name == "COVERED_CONTRAST");
        Assert.False(covered.Tuned);
        Assert.Contains("hand", covered.Explanation);
    }

    [Fact]
    public void Report_lines_have_every_value_warning_and_the_decision()
    {
        var report = new EyeCheckReport
        {
            Values = GoodValues(),
            Warnings = ["The camera is slow."],
            Decision = "Used the values above.",
        };
        var lines = report.Lines().ToList();
        Assert.Equal(7 + 2, lines.Count);
        Assert.StartsWith("Resting head angle, up/down: ", lines[0]);
        Assert.Equal("Used the values above.", lines[^1]);
    }
}

public class SummaryTests
{
    [Fact]
    public void Spread_needs_three_values_and_skips_missing_ones()
    {
        Assert.Null(Spread.Of([1, 2]));
        Assert.Null(Spread.Of([1, null, double.NaN, 2]));
        var s = Spread.Of([1, 2, 3, 4, 5, null])!;
        Assert.Equal(3, s.Median);
        Assert.Equal(1.04, s.P1, 3);
        Assert.Equal(4.96, s.P99, 3);
    }

    [Fact]
    public void Step_report_counts_tries_faces_and_eyes()
    {
        var frames = F.Many(40, i => i < 30 ? F.Frame(1200 + i * 33, F.Open(), i < 20 ? F.Open() : null)
                                            : F.Frame(1200 + i * 33, null, null, face: false));
        var step = EyeCheckSteps.All[0];

        var report = StepReport.Of(step, 2, StepCheck.Fail("too few"), frames);

        Assert.Equal("Rest", report.Step);
        Assert.Equal(2, report.Tries);
        Assert.False(report.Passed);
        Assert.Equal("too few", report.Problem);
        Assert.Equal(40, report.Frames);
        Assert.Equal(0.75, report.FaceShare);
        Assert.Equal(1.0, report.Left!.FoundShare);
        Assert.Equal(0.667, report.Right!.FoundShare);
        Assert.Equal(0.35, report.Left.Openness!.Median);
        Assert.Equal(-10, report.Pitch!.Median);
        Assert.Equal(30, report.CaptureFps);
    }

    [Fact]
    public void Gaze_target_uses_only_frames_with_both_eyes_open()
    {
        var target = new CalibrationTarget(3, 0.92, 0.5);
        var closed = new EyeSample(0.9, 0.4, 0.6, 0.1, 60, true, false, 0.9, 0.9);
        var frames = F.Many(30, i => F.Frame(i * 33, i < 25 ? F.Open(h: 0.2, v: -0.05) : closed, F.Open(h: 0.18, v: -0.04)));

        var summary = GazeTargetSummary.Of(target, frames);

        Assert.Equal(3, summary.Index);
        Assert.Equal(0.92, summary.X);
        Assert.Equal(30, summary.Frames);
        Assert.Equal(25, summary.UsableFrames);
        Assert.Equal(0.2, summary.LeftH!.Median);   // the closed frames' 0.9 left out
        Assert.Equal(-0.04, summary.RightV!.Median);
    }

    [Fact]
    public void Frame_keeps_the_iris_position_for_the_gaze_fit()
    {
        var json = """
            {"type":"frame","t":1,"faces":[{"box":[0,0,1,1],"landmarks":[],"irises":{"left":[0,0],"right":[0,0]},
             "pose":{"yaw":2,"pitch":-9,"roll":4},"blendshapes":{},
             "eyes":{"left":{"offset":{"h":0.21,"v":-0.07},"closed":false,"openness":0.33,"width_px":60},
                     "gaze":{"raw":{"h":0.2,"v":0},"h":0.2,"v":0,"direction":"right","binocular_error":{"h":null,"v":null},"eyes_used":1}}}]}
            """;
        var msg = JsonSerializer.Deserialize<TrackerMessage>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var f = FrameSample.From(msg, 0);
        Assert.Equal(0.21, f.Left!.OffsetH);
        Assert.Equal(-0.07, f.Left.OffsetV);
        Assert.Equal(4, f.Roll);
    }
}

public sealed class RecordingStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mouSEE-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static CalibrationRecording Sample() => new()
    {
        EyeCheck = [new StepRecording("Rest", 1, true, null, [F.Frame(0, F.Open(0.1), F.Open(0.1)), F.Frame(33, null, null, face: false)])],
        CheckRound = [new TrialRecording("OpenBoth", [F.Frame(1300, F.Open(), F.Open())])],
        Gaze = [new TargetRecording(0, 0.08, 0.08, false, [F.Frame(0, F.Open(-0.2, -0.1), F.Open(-0.2, -0.1))])],
    };

    [Fact]
    public void Recording_is_saved_next_to_the_calibration_and_read_back()
    {
        var store = new CalibrationStore(_dir);
        var saved = store.Add(new CalibrationProfile());

        store.SaveRecording(saved.Id, Sample());
        var back = store.LoadRecording(saved.Id)!;

        Assert.Equal(Path.Combine(_dir, "recordings", saved.Id + ".json"), store.RecordingFileFor(saved.Id));
        Assert.Equal(Sample().EyeCheck[0].Frames, back.EyeCheck[0].Frames);
        Assert.Equal(-0.2, back.Gaze[0].Frames[0].Left!.OffsetH);
        Assert.Single(new CalibrationStore(_dir).All); // the recording isn't taken for a calibration
    }

    [Fact]
    public void Deleting_a_calibration_deletes_its_recording()
    {
        var store = new CalibrationStore(_dir);
        var saved = store.Add(new CalibrationProfile());
        store.SaveRecording(saved.Id, Sample());

        store.Delete(saved.Id);

        Assert.False(File.Exists(store.RecordingFileFor(saved.Id)));
    }

    [Fact]
    public void Deleting_one_without_a_recording_works()
    {
        var store = new CalibrationStore(_dir);
        var saved = store.Add(new CalibrationProfile());
        store.Delete(saved.Id);
        Assert.Empty(store.All);
    }

    [Fact]
    public void No_recording_is_null()
    {
        Assert.Null(new CalibrationStore(_dir).LoadRecording("nothing"));
    }

    [Fact]
    public void Report_and_targets_are_in_the_calibration_file()
    {
        var store = new CalibrationStore(_dir);
        store.Add(new CalibrationProfile
        {
            EyeCheck = new EyeCheckReport
            {
                Values = [new EyeValueReport { Name = "MAX_YAW", Used = 35, Default = 35, Measured = new() { ["turnedFurthest"] = 18 } }],
                Steps = [new StepReport { Step = "Rest", Tries = 1, Passed = true }],
                Check = new CheckRoundReport { Passed = true, Scores = new() { ["both eyes open"] = 0.97 } },
            },
            GazeTargets = [new GazeTargetSummary { Index = 4, X = 0.5, Y = 0.5, UsableFrames = 30 }],
        });

        var loaded = new CalibrationStore(_dir).Active!;
        Assert.Equal(18, loaded.EyeCheck!.Values[0].Measured["turnedFurthest"]);
        Assert.Equal(0.97, loaded.EyeCheck.Check!.Scores["both eyes open"]);
        Assert.Equal(4, loaded.GazeTargets[0].Index);
        Assert.Contains("\"turnedFurthest\": 18", File.ReadAllText(store.ActiveFile!));
    }
}

public class LookChoiceTests
{
    private long _now;
    private readonly List<bool> _chosen = [];

    private LookChoice Make()
    {
        var c = new LookChoice(() => _now);
        c.Chosen += yes => _chosen.Add(yes);
        return c;
    }

    // a frame with the eyes looking this far right (+) or left (-), null = eyes closed
    private static TrackerMessage Looking(double? h) => new("frame", 0,
        [new Face([0, 0, 1, 1], [], new Irises([0, 0], [0, 0]), new Pose(0, 0, 0), [],
                  new Eyes(null!, null!, new GazeData(new GazeRaw(h, 0), h, 0, "", new GazeError(null, null), h is null ? 0 : 2)))],
        null, 0, 0);

    private void Hold(LookChoice c, double? h, long ms, long step = 33)
    {
        for (long t = 0; t <= ms; t += step)
        {
            c.OnFrame(Looking(h));
            _now += step;
        }
    }

    [Fact]
    public void Looking_right_for_three_seconds_is_yes()
    {
        var c = Make();
        Hold(c, 0, 200);
        Hold(c, 0.2, 3100);
        Assert.Equal([true], _chosen);
    }

    [Fact]
    public void Looking_left_is_no()
    {
        var c = Make();
        Hold(c, 0, 200);
        Hold(c, -0.2, 3100);
        Assert.Equal([false], _chosen);
    }

    [Fact]
    public void Not_before_the_eyes_were_in_the_middle()
    {
        // already looking right when the question shows up: nothing until they come back to the middle
        var c = Make();
        Hold(c, 0.2, 4000);
        Assert.Empty(_chosen);
        Assert.False(c.Armed);

        Hold(c, 0, 100);
        Hold(c, 0.2, 3100);
        Assert.Equal([true], _chosen);
    }

    [Fact]
    public void Closed_eyes_dont_count_as_the_middle()
    {
        var c = Make();
        Hold(c, null, 500);
        Assert.False(c.Armed);
    }

    [Fact]
    public void Not_held_long_enough_is_nothing()
    {
        var c = Make();
        Hold(c, 0, 100);
        Hold(c, 0.2, 2500);
        Hold(c, 0, 100);
        Assert.Empty(_chosen);
        Assert.Equal(0, c.Progress);
    }

    [Fact]
    public void A_blink_while_holding_doesnt_start_over()
    {
        var c = Make();
        Hold(c, 0, 100);
        Hold(c, 0.2, 1500);
        Hold(c, null, 200); // blink
        Hold(c, 0.2, 1400);
        Assert.Equal([true], _chosen);
    }

    [Fact]
    public void Switching_sides_starts_over()
    {
        var c = Make();
        Hold(c, 0, 100);
        Hold(c, 0.2, 2000);
        Hold(c, -0.2, 2000);
        Assert.Empty(_chosen);
        Assert.Equal(LookSide.Left, c.Side);
    }

    [Fact]
    public void Wobbling_at_the_threshold_keeps_counting()
    {
        // 0.1 isn't enough to start (0.12) but is enough to stay (0.08)
        var c = Make();
        Hold(c, 0, 100);
        Hold(c, 0.13, 500);
        Hold(c, 0.1, 2700);
        Assert.Equal([true], _chosen);
    }

    [Fact]
    public void One_answer_then_back_to_the_middle_for_the_next()
    {
        var c = Make();
        Hold(c, 0, 100);
        Hold(c, 0.2, 6500);
        Assert.Equal([true], _chosen); // not twice

        Hold(c, 0, 100);
        Hold(c, -0.2, 3100);
        Assert.Equal([true, false], _chosen);
    }

    [Fact]
    public void Progress_fills_while_holding()
    {
        var c = Make();
        Hold(c, 0, 100);
        Hold(c, 0.2, 1500);
        Assert.Equal(LookSide.Right, c.Side);
        Assert.InRange(c.Progress, 0.45, 0.55);
    }
}
