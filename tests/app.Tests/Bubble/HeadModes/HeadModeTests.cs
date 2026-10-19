namespace app.Tests;

public class HeadModeTests
{
    [Theory]
    [InlineData(HeadMode.Cancel, true, false)]
    [InlineData(HeadMode.Nudge, false, true)]
    [InlineData(HeadMode.Both, true, true)]
    public void EachMode_TurnsOnItsParts(HeadMode mode, bool cancels, bool nudges)
    {
        Assert.Equal(cancels, mode.Cancels());
        Assert.Equal(nudges, mode.Nudges());
    }

    [Fact]
    public void Default_IsHowItWasBefore()
    {
        Assert.Equal(HeadMode.Cancel, new AppSettings().Head.Mode);
    }

    [Fact]
    public void OnlyTheNudgeMode_TellsTheTrackerNotToCancel()
    {
        AppSettings With(HeadMode mode) => new() { Head = new() { Mode = mode } };
        Assert.DoesNotContain("--no-head-compensation", TrackerArgs.Build(With(HeadMode.Cancel), null, []));
        Assert.Contains("--no-head-compensation", TrackerArgs.Build(With(HeadMode.Nudge), null, []));
        Assert.DoesNotContain("--no-head-compensation", TrackerArgs.Build(With(HeadMode.Both), null, []));
    }

    [Fact]
    public void Modes_SaveByName()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new HeadSettings { Mode = HeadMode.Both });
        Assert.Contains("\"both\"", json);
        Assert.All(HeadModeExtensions.All, m => Assert.False(string.IsNullOrEmpty(m.Label())));
    }
}
