using System.Text.Json;

namespace app.Tests;

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
        var frames = FakeFrames.Many(40, i => i < 30 ? FakeFrames.Frame(1200 + i * 33, FakeFrames.Open(), i < 20 ? FakeFrames.Open() : null)
                                            : FakeFrames.Frame(1200 + i * 33, null, null, face: false));
        var step = EyeCheckSteps.All[0];

        var report = StepReport.Of(step, 2, StepCheck.Fail("too few"), frames);

        Assert.Equal("CloseBoth", report.Step);
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
        var frames = FakeFrames.Many(30, i => FakeFrames.Frame(i * 33, i < 25 ? FakeFrames.Open(h: 0.2, v: -0.05) : closed, FakeFrames.Open(h: 0.18, v: -0.04)));

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
