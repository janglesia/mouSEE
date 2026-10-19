namespace app.Tests;

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
            run[step.Kind] = FakeFrames.Many(100, i => FakeFrames.Frame(1200 + i * 33, Eye(l, i), Eye(r, i),
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
