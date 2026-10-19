namespace app.Tests;

public class HeadModeTests
{
    [Theory]
    [InlineData(HeadMode.EyesOnly, true, false)]
    [InlineData(HeadMode.NudgeNoCorrection, false, true)]
    [InlineData(HeadMode.EyesAndNudge, true, true)]
    public void EachMode_TurnsOnItsParts(HeadMode mode, bool cancels, bool nudges)
    {
        Assert.Equal(cancels, mode.Cancels());
        Assert.Equal(nudges, mode.Nudges());
    }

    [Fact]
    public void Default_IsHowItWasBefore()
    {
        Assert.Equal(HeadMode.EyesOnly, new AppSettings().Head.Mode);
    }

    [Fact]
    public void OnlyTheNudgeMode_TellsTheTrackerNotToCancel()
    {
        AppSettings With(HeadMode mode) => new() { Head = new() { Mode = mode } };
        Assert.DoesNotContain("--no-head-compensation", TrackerArgs.Build(With(HeadMode.EyesOnly), null, []));
        Assert.Contains("--no-head-compensation", TrackerArgs.Build(With(HeadMode.NudgeNoCorrection), null, []));
        Assert.DoesNotContain("--no-head-compensation", TrackerArgs.Build(With(HeadMode.EyesAndNudge), null, []));
    }

    [Fact]
    public void Modes_SaveByName()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new HeadSettings { Mode = HeadMode.EyesAndNudge });
        Assert.Contains("\"both\"", json);
        Assert.All(HeadModeExtensions.All, m => Assert.False(string.IsNullOrEmpty(m.Label())));
    }
}
