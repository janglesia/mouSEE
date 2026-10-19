using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using app.Calibration;

namespace app.Tests;

// fake eye check recordings, with numbers like the ones in docs/EYE-CALIBRATION.md ("Reference numbers")
file static class Rec
{
    public static EyeSample Open(int i = 0) => new(0.25 + i % 10 * 0.008, 0.55 + i % 7 * 0.02, 0.05, 0.35, 60, false, false);
    public static EyeSample Closed(int i = 0, double blink = 0.3) => new(0.92 + i % 5 * 0.01, 0.4 + i % 6 * 0.03, blink, 0.2, 60, true, false);

    public static List<FrameSample> Frames(int count, Func<int, (EyeSample? L, EyeSample? R)> eyes,
                                           double pitch = -10, double yaw = 2, bool face = true, double fps = 30,
                                           Func<int, double>? pitchAt = null, Func<int, double>? yawAt = null) =>
        Enumerable.Range(0, count).Select(i =>
        {
            var (l, r) = eyes(i);
            // starting after the reaction time, 33 ms apart
            return new FrameSample(EyeCheckSteps.ReactionMs + i * 33, face, yawAt?.Invoke(i) ?? yaw,
                                   pitchAt?.Invoke(i) ?? pitch, face ? l : null, face ? r : null, fps);
        }).ToList();

    public static EyeCheckStep Step(EyeCheckStepKind kind) => EyeCheckSteps.All.First(s => s.Kind == kind);

    public static readonly EyeCheckBaseline Baseline = new(-10, 2);

    // every step done right
    public static Dictionary<EyeCheckStepKind, IReadOnlyList<FrameSample>> GoodRun() => new()
    {
        [EyeCheckStepKind.Rest] = Frames(90, i => (Open(i), Open(i))),
        [EyeCheckStepKind.LookAround] = Frames(200, i => (Open(i), Open(i)), pitchAt: i => -10 + (i % 20 - 10), yawAt: i => 2 + (i % 30 - 15)),
        [EyeCheckStepKind.CloseLeft] = Frames(110, i => (Closed(i), Open(i))),
        [EyeCheckStepKind.CloseRight] = Frames(110, i => (Open(i), Closed(i))),
        [EyeCheckStepKind.CloseLeftHeadUp] = Frames(110, i => (Closed(i, blink: 0.37), Open(i)), pitch: -33),
        [EyeCheckStepKind.CloseRightHeadDown] = Frames(110, i => (Open(i), Closed(i)), pitch: 10),
        [EyeCheckStepKind.CloseBoth] = Frames(80, i => (Closed(i, 0.7), Closed(i, 0.7))),
    };
}

public class EyeCheckStepsTests
{
    private static StepCheck Check(EyeCheckStepKind kind, List<FrameSample> frames) =>
        EyeCheckSteps.Check(Rec.Step(kind), frames, Rec.Baseline);

    [Fact]
    public void Steps_are_in_the_order_of_the_doc()
    {
        Assert.Equal(Enum.GetValues<EyeCheckStepKind>(), EyeCheckSteps.All.Select(s => s.Kind));
    }

    [Fact]
    public void Reaction_time_is_thrown_away()
    {
        var frames = Enumerable.Range(0, 50).Select(i => new FrameSample(i * 50, true, 0, 0, null, null, 30)).ToList();
        Assert.All(EyeCheckSteps.Usable(frames), f => Assert.True(f.Ms >= 1200));
        Assert.Equal(26, EyeCheckSteps.Usable(frames).Count);
    }

    [Fact]
    public void Good_recordings_pass_every_step()
    {
        foreach (var (kind, frames) in Rec.GoodRun())
            Assert.True(Check(kind, frames.ToList()).Ok, $"{kind}: {Check(kind, frames.ToList()).Problem}");
    }

    [Fact]
    public void Too_few_frames_fails()
    {
        Assert.False(Check(EyeCheckStepKind.Rest, Rec.Frames(5, i => (Rec.Open(), Rec.Open()))).Ok);
    }

    [Fact]
    public void Face_missing_fails()
    {
        var frames = Rec.Frames(100, i => (Rec.Open(), Rec.Open()), face: false);
        Assert.Contains("face", Check(EyeCheckStepKind.CloseLeft, frames).Problem);
    }

    [Fact]
    public void Wink_with_the_wrong_eye_fails()
    {
        var frames = Rec.Frames(100, i => (Rec.Open(i), Rec.Closed(i)));
        Assert.Equal("Close your left eye all the way.", Check(EyeCheckStepKind.CloseLeft, frames).Problem);
    }

    [Fact]
    public void Both_closed_when_one_was_asked_fails()
    {
        var frames = Rec.Frames(100, i => (Rec.Closed(i, 0.7), Rec.Closed(i, 0.7)));
        Assert.Contains("Keep your right eye open", Check(EyeCheckStepKind.CloseLeft, frames).Problem);
    }

    [Fact]
    public void Closed_eye_counts_by_iris_ratio_when_blink_scores_are_the_same()
    {
        // tilting the head up makes the blink score of both eyes ~0.37, the iris ratio still shows it
        var sameBlink = Rec.Frames(100, i => (Rec.Closed(i, 0.37), Rec.Open(i) with { Blink = 0.38 }), pitch: -33);
        Assert.True(Check(EyeCheckStepKind.CloseLeftHeadUp, sameBlink).Ok);
    }

    [Fact]
    public void Head_has_to_tilt_the_right_way()
    {
        var notTilted = Rec.Frames(100, i => (Rec.Closed(i), Rec.Open(i)), pitch: -15);
        Assert.Contains("further up", Check(EyeCheckStepKind.CloseLeftHeadUp, notTilted).Problem);

        // tilted down when up was asked
        var wrongWay = Rec.Frames(100, i => (Rec.Closed(i), Rec.Open(i)), pitch: 10);
        Assert.False(Check(EyeCheckStepKind.CloseLeftHeadUp, wrongWay).Ok);

        var down = Rec.Frames(100, i => (Rec.Open(i), Rec.Closed(i)), pitch: -12);
        Assert.Contains("further down", Check(EyeCheckStepKind.CloseRightHeadDown, down).Problem);
    }

    [Fact]
    public void Tilt_is_measured_from_resting()
    {
        // -26 is only 16 up from a resting -10, but that's enough
        var frames = Rec.Frames(100, i => (Rec.Closed(i), Rec.Open(i)), pitch: -26);
        Assert.True(Check(EyeCheckStepKind.CloseLeftHeadUp, frames).Ok);
    }

    [Fact]
    public void Both_eyes_have_to_close()
    {
        var oneOpen = Rec.Frames(100, i => (Rec.Closed(i, 0.7), Rec.Open(i)));
        Assert.False(Check(EyeCheckStepKind.CloseBoth, oneOpen).Ok);
    }

    [Fact]
    public void Look_around_needs_both_eyes_open()
    {
        var closed = Rec.Frames(100, i => (Rec.Closed(i, 0.7), Rec.Open(i)));
        Assert.False(Check(EyeCheckStepKind.LookAround, closed).Ok);
    }

    [Fact]
    public void Live_hint_says_what_to_fix_and_nothing_when_it_is_right()
    {
        var step = Rec.Step(EyeCheckStepKind.CloseLeftHeadUp);
        var right = Rec.Frames(25, i => (Rec.Closed(i), Rec.Open(i)), pitch: -33);
        var notTilted = Rec.Frames(25, i => (Rec.Closed(i), Rec.Open(i)), pitch: -10);

        Assert.Null(EyeCheckSteps.LiveHint(step, right, Rec.Baseline));
        Assert.Contains("further up", EyeCheckSteps.LiveHint(step, notTilted, Rec.Baseline));
        Assert.Null(EyeCheckSteps.LiveHint(step, right.Take(3).ToList(), Rec.Baseline)); // too little to say anything
    }
}

public class EyeCheckAnalysisTests
{
    [Fact]
    public void Good_run_tunes_everything_between_open_and_closed()
    {
        var outcome = EyeCheckAnalysis.Analyze(Rec.GoodRun());
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
        var t = EyeCheckAnalysis.Analyze(Rec.GoodRun()).Thresholds;
        Assert.InRange(t.LidsOpenOpenness!.Value, 0.22, 0.33);
    }

    [Fact]
    public void Glasses_reflections_still_tune_the_lids()
    {
        // what happened with glasses: with the screen reflecting on them the open iris ratio overlaps the closed one,
        // so IRIS_HIDDEN_RATIO can't be tuned. The lids aren't affected and still separate open from closed
        var run = Rec.GoodRun();
        run[EyeCheckStepKind.LookAround] = Rec.Frames(200, i => (Rec.Open(i) with { IrisRatio = 0.6 + i % 10 * 0.03 },
                                                                 Rec.Open(i) with { IrisRatio = 0.6 + i % 10 * 0.03 }));
        var outcome = EyeCheckAnalysis.Analyze(run);

        Assert.Null(outcome.Thresholds.IrisHiddenRatio);
        Assert.NotNull(outcome.Thresholds.LidsOpenOpenness);
        Assert.True(outcome.TunedDetection); // so the check round runs
    }

    [Fact]
    public void Lids_too_close_keep_the_default()
    {
        var run = Rec.GoodRun();
        // looking down brings the lid down to about where a closed eye is
        run[EyeCheckStepKind.LookAround] = Rec.Frames(200, i => (Rec.Open(i) with { Openness = i % 4 == 0 ? 0.21 : 0.35 },
                                                                 Rec.Open(i)));
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
        var run = Rec.GoodRun();
        run[EyeCheckStepKind.LookAround] = Rec.Frames(200, i => (Rec.Open(i) with { IrisRatio = 0.8 }, Rec.Open(i) with { IrisRatio = 0.8 }));
        foreach (var kind in new[] { EyeCheckStepKind.CloseLeft, EyeCheckStepKind.CloseRight, EyeCheckStepKind.CloseLeftHeadUp,
                                     EyeCheckStepKind.CloseRightHeadDown, EyeCheckStepKind.CloseBoth })
            run[kind] = Rec.Frames(100, i => (Rec.Closed(i) with { IrisRatio = 1.2 }, Rec.Closed(i) with { IrisRatio = 1.2 }));

        Assert.Equal(EyeCheckAnalysis.IrisMax, EyeCheckAnalysis.Analyze(run).Thresholds.IrisHiddenRatio);
    }

    [Fact]
    public void Open_and_closed_too_alike_keeps_the_default()
    {
        var run = Rec.GoodRun();
        run[EyeCheckStepKind.LookAround] = Rec.Frames(200, i => (Rec.Open(i) with { IrisRatio = 0.85 }, Rec.Open(i) with { IrisRatio = 0.85 }));

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
        var run = Rec.GoodRun();
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
        var t = EyeCheckAnalysis.Analyze(Rec.GoodRun()).Thresholds;
        Assert.Equal(EyeCheckAnalysis.DefaultMaxPitch, t.MaxPitch);
        Assert.Equal(EyeCheckAnalysis.DefaultMaxYaw, t.MaxYaw);
    }

    [Fact]
    public void Head_limits_grow_when_looking_at_the_corners_turns_the_head_a_lot()
    {
        // turning 30° left and right to look at the corners, the default 35° would leave only 5° to spare
        var run = Rec.GoodRun();
        run[EyeCheckStepKind.LookAround] = Rec.Frames(200, i => (Rec.Open(i), Rec.Open(i)), yawAt: i => 2 + (i % 2 == 0 ? 30 : -30));

        var t = EyeCheckAnalysis.Analyze(run).Thresholds;

        Assert.Equal(40, t.MaxYaw);
        Assert.Equal(EyeCheckAnalysis.DefaultMaxPitch, t.MaxPitch);
    }

    [Fact]
    public void Warns_about_small_eyes_and_a_slow_camera()
    {
        var run = Rec.GoodRun();
        run[EyeCheckStepKind.Rest] = Rec.Frames(90, i => (Rec.Open(i) with { WidthPx = 20 }, Rec.Open(i) with { WidthPx = 20 }), fps: 15);

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

public class EyeCheckRoundTests
{
    [Fact]
    public void Every_instruction_twice_never_the_same_twice_in_a_row()
    {
        for (int seed = 0; seed < 20; seed++)
        {
            var order = EyeCheckRound.Order(new Random(seed));
            Assert.Equal(6, order.Count);
            Assert.All(Enum.GetValues<CheckTrial>(), t => Assert.Equal(2, order.Count(o => o == t)));
            Assert.DoesNotContain(order.Zip(order.Skip(1)), p => p.First == p.Second);
        }
    }

    [Fact]
    public void Matches_uses_the_tracker_decisions()
    {
        FrameSample F(EyeSample? l, EyeSample? r) => new(2000, true, 0, -10, l, r, 30);
        Assert.True(EyeCheckRound.Matches(CheckTrial.CloseLeft, F(Rec.Closed(), Rec.Open())));
        Assert.False(EyeCheckRound.Matches(CheckTrial.CloseLeft, F(Rec.Closed(), Rec.Closed())));
        Assert.True(EyeCheckRound.Matches(CheckTrial.CloseRight, F(Rec.Open(), Rec.Closed())));
        Assert.True(EyeCheckRound.Matches(CheckTrial.OpenBoth, F(Rec.Open(), Rec.Open())));
        Assert.False(EyeCheckRound.Matches(CheckTrial.OpenBoth, F(Rec.Open(), null)));
        // nothing was in front of the eye, so the tracker calling it covered is wrong
        Assert.False(EyeCheckRound.Matches(CheckTrial.OpenBoth, F(Rec.Open() with { Covered = true }, Rec.Open())));
        Assert.False(EyeCheckRound.Matches(CheckTrial.CloseLeft, F(Rec.Closed() with { Covered = true }, Rec.Open())));
        Assert.False(EyeCheckRound.Matches(CheckTrial.OpenBoth, new FrameSample(2000, false, 0, 0, null, null, 30)));
    }

    [Fact]
    public void Score_and_pass()
    {
        var good = Rec.Frames(60, i => (Rec.Closed(i), Rec.Open(i)));
        // 85% right
        var someMissed = Rec.Frames(60, i => i % 20 < 17 ? (Rec.Open(i), Rec.Open(i)) : (Rec.Closed(i), Rec.Open(i)));

        var scores = EyeCheckRound.Score([(CheckTrial.CloseLeft, good), (CheckTrial.OpenBoth, someMissed)]);

        Assert.Equal(1.0, scores[CheckTrial.CloseLeft]);
        Assert.Equal(0.85, scores[CheckTrial.OpenBoth], 2);
        Assert.False(EyeCheckRound.Passed(scores));
        Assert.True(EyeCheckRound.Passed(new Dictionary<CheckTrial, double> { [CheckTrial.CloseLeft] = 0.95 }));
        Assert.False(EyeCheckRound.Passed(new Dictionary<CheckTrial, double>()));
    }
}

public class StatsTests
{
    [Fact]
    public void Percentile_interpolates_like_numpy()
    {
        double[] values = [1, 2, 3, 4, 5];
        Assert.Equal(3, Stats.Percentile(values, 50));
        Assert.Equal(1, Stats.Percentile(values, 0));
        Assert.Equal(5, Stats.Percentile(values, 100));
        Assert.Equal(4.96, Stats.Percentile(values, 99), 6); // numpy.percentile([1,2,3,4,5], 99)
        Assert.Equal(1.2, Stats.Percentile(values, 5), 6);
        Assert.Equal(7, Stats.Percentile([7], 30));
    }
}

public class TrackerEyeFieldsTests
{
    [Fact]
    public void Eye_check_fields_are_read_from_a_frame()
    {
        // the per-eye part of a frame as eye_extractor.py sends it (trimmed)
        string json = """
            {"type":"frame","t":1,"capture_fps":30,"faces":[{"box":[0,0,1,1],"landmarks":[],"irises":{"left":[0,0],"right":[0,0]},
             "pose":{"yaw":2,"pitch":-9,"roll":0},"blendshapes":{},
             "eyes":{"left":{"offset":{"h":0.1,"v":0},"closed":true,"openness":0.2,"blink":0.31,"contrast":0.42,"iris_ratio":0.93,"covered":false,"width_px":58.5},
                     "gaze":{"raw":{"h":null,"v":null},"h":null,"v":null,"direction":"closed","binocular_error":{"h":null,"v":null},"eyes_used":0}}}]}
            """;
        var msg = JsonSerializer.Deserialize<TrackerMessage>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var frame = FrameSample.From(msg, 1500);

        Assert.True(frame.FaceFound);
        Assert.Equal(-9, frame.Pitch);
        Assert.Equal(new EyeSample(0.93, 0.42, 0.31, 0.2, 58.5, true, false, 0.1, 0), frame.Left);
        Assert.Null(frame.Right); // dropped eyes are missing from the message
        Assert.Equal(30, frame.CaptureFps);
    }

    [Fact]
    public void No_face_is_a_frame_without_a_face()
    {
        var msg = new TrackerMessage("frame", 1, [], null, 0, 0);
        Assert.False(FrameSample.From(msg, 0).FaceFound);
    }
}
