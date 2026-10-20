using System.IO;
using System.Text.Json;

namespace app.Tests;

public class AccuracyTestTests
{
    private static FrameSample Bubble(double? x, double? y = 0.5, double faceX = 0.47) =>
        new(0, true, 2, -15, null, null, 30, 0, faceX, 0.4, 0.3, x, y);

    [Fact]
    public void Points_are_five_columns_then_leaning_both_ways()
    {
        var points = AccuracyTest.Points;
        Assert.Equal(20 + 3 + 3 + 3 + 3, points.Count);
        // 4 rows sitting normally, the fine-tune needs them for its up/down curves
        Assert.Equal(4, points.Where(p => p.Phase == AccuracyTest.Normal).Select(p => p.Y).Distinct().Count());
        Assert.Equal(5, points.Where(p => p.Phase == AccuracyTest.Normal).Select(p => p.X).Distinct().Count());
        Assert.Equal(3, points.Count(p => p.Phase == AccuracyTest.LeanLeft));
        Assert.Equal(3, points.Count(p => p.Phase == AccuracyTest.LeanRight));
        // tilting: top, middle and bottom
        Assert.Equal([0.15, 0.5, 0.85], points.Where(p => p.Phase == AccuracyTest.HeadUp).Select(p => p.Y));
        Assert.Equal([0.85, 0.5, 0.15], points.Where(p => p.Phase == AccuracyTest.HeadDown).Select(p => p.Y));
        Assert.Null(AccuracyTest.Pose(AccuracyTest.Normal));
        Assert.Equal(HeadPose.HeadUp, AccuracyTest.Pose(AccuracyTest.HeadUp));
        // snake order: the second row runs right to left
        Assert.Equal(0.95, points[5].X);
    }

    [Fact]
    public void Result_is_the_median_bubble_against_the_point()
    {
        var frames = new[] { 0.31, 0.32, 0.30, 0.9, 0.33 }.Select(x => Bubble(x)).Append(Bubble(null, null)).ToList();
        var r = AccuracyTest.Result(new TestPoint(AccuracyTest.Normal, 0.275, 0.5), frames);

        Assert.Equal(6, r.Frames);
        Assert.Equal(5, r.Estimates);
        Assert.Equal(0.32, r.EstimateX!.Median);
        Assert.Equal(0.045, r.ErrorX);     // one glance away (0.9) doesn't move the median
        Assert.Equal(0.0, r.ErrorY);
    }

    [Fact]
    public void No_bubble_at_all_has_no_error()
    {
        var r = AccuracyTest.Result(new TestPoint(AccuracyTest.Normal, 0.5, 0.5), [Bubble(null, null), Bubble(null, null)]);
        Assert.Null(r.ErrorX);
        Assert.Equal(0, r.Estimates);
    }

    [Fact]
    public void Report_gives_error_and_direction_per_phase()
    {
        TestPointResult P(string phase, double x, double? err) => new() { Phase = phase, X = x, Y = 0.5, ErrorX = err, Estimates = err is null ? 0 : 40 };
        var report = AccuracyTest.Report(
        [
            P(AccuracyTest.Normal, 0.05, 0.02), P(AccuracyTest.Normal, 0.95, -0.04),
            P(AccuracyTest.LeanLeft, 0.5, 0.08), P(AccuracyTest.LeanLeft, 0.9, 0.06),
            P(AccuracyTest.LeanRight, 0.5, null),
        ], null, 2560, 1440);

        Assert.Equal(0.03, report.MeanErrorX[AccuracyTest.Normal]);
        Assert.Equal(-0.01, report.BiasX[AccuracyTest.Normal]);
        Assert.Equal(0.07, report.MeanErrorX[AccuracyTest.LeanLeft]);
        Assert.Contains(report.Summary, s => s.StartsWith("Lean left") && s.Contains("too far right"));
        Assert.Contains(report.Summary, s => s.Contains("0.95: -0.040"));
        Assert.Contains(report.Summary, s => s.Contains("1 point(s) had no bubble"));
        Assert.False(report.MeanErrorX.ContainsKey(AccuracyTest.LeanRight));
    }

    [Fact]
    public void Frame_keeps_where_the_bubble_was()
    {
        string json = """
            {"type":"frame","t":1,"faces":[{"box":[0,0,1,1],"landmarks":[],"irises":{"left":[0,0],"right":[0,0]},
             "pose":{"yaw":0,"pitch":0,"roll":0},"blendshapes":{},
             "eyes":{"gaze":{"raw":{"h":0,"v":0},"h":0,"v":0,"direction":"center","binocular_error":{"h":null,"v":null},"eyes_used":2},
                     "estimate":{"valid":true,"reason":null,"h_deg":1,"v_deg":0,"x":0.61,"y":0.42,"eyes_used":2}}}]}
            """;
        var msg = JsonSerializer.Deserialize<TrackerMessage>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var f = FrameSample.From(msg, 0);
        Assert.Equal(0.61, f.EstimateX);
        Assert.Equal(0.42, f.EstimateY);
    }

    [Fact]
    public void Test_is_saved_in_tests()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mouSEE-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new CalibrationStore(dir);
            var report = AccuracyTest.Report([], null, 1920, 1080);
            string path = store.SaveTest(new AccuracyTestFile(report, [new TargetRecording(0, 0.05, 0.15, false, [Bubble(0.06)])]));

            Assert.Equal(Path.Combine(dir, "tests"), Path.GetDirectoryName(path));
            Assert.Contains("\"estimateX\":0.06", File.ReadAllText(path));
            Assert.Empty(new CalibrationStore(dir).All); // not taken for a calibration
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
