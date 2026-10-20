namespace app.Tests;

public class ValueReportTests
{
    private static List<EyeValueReport> GoodValues() =>
        EyeCheckAnalysis.Analyze(FakeEyeCheck.Grid(openness: i => 0.2 + i % 4 * 0.05), FakeEyeCheck.GoodRun()).Values;

    [Fact]
    public void Every_value_is_reported_once_with_its_default()
    {
        var values = GoodValues();
        Assert.Equal(["REST_PITCH", "REST_YAW", "LIDS_SHUT_OPENNESS", "IRIS_HIDDEN_RATIO", "LIDS_OPEN_OPENNESS", "COVERED_CONTRAST"],
                     values.Select(v => v.Name));
        Assert.All(values, v =>
        {
            Assert.NotNull(v.Default);
            Assert.False(string.IsNullOrWhiteSpace(v.Label));
            Assert.False(string.IsNullOrWhiteSpace(v.Explanation));
        });
    }

    [Fact]
    public void Tuned_value_says_what_it_replaced_and_why()
    {
        var lids = GoodValues().Single(v => v.Name == "LIDS_SHUT_OPENNESS");

        Assert.True(lids.Tuned);
        Assert.Contains("instead of the default 0.1", lids.Explanation);
        Assert.Contains("halfway between", lids.Explanation);
        Assert.True(lids.Measured["openLowest"] > lids.Used && lids.Used > lids.Measured["closedHighest"]);
    }

    [Fact]
    public void Wink_values_say_why_they_arent_tuned()
    {
        var values = GoodValues();
        foreach (var name in new[] { "IRIS_HIDDEN_RATIO", "LIDS_OPEN_OPENNESS" })
        {
            var v = values.Single(x => x.Name == name);
            Assert.False(v.Tuned);
            Assert.Contains("wink", v.Explanation);
        }
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
        Assert.Equal(6 + 2, lines.Count);
        Assert.StartsWith("Resting head angle, up/down: ", lines[0]);
        Assert.Equal("Used the values above.", lines[^1]);
    }
}
