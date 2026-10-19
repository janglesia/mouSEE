namespace app.Tests;

public class EyeCheckStepsTests
{
    private static StepCheck Check(EyeCheckStepKind kind, List<FrameSample> frames) =>
        EyeCheckSteps.Check(FakeEyeCheck.Step(kind), frames, FakeEyeCheck.Baseline);

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
        foreach (var (kind, frames) in FakeEyeCheck.GoodRun())
            Assert.True(Check(kind, frames.ToList()).Ok, $"{kind}: {Check(kind, frames.ToList()).Problem}");
    }

    [Fact]
    public void Too_few_frames_fails()
    {
        Assert.False(Check(EyeCheckStepKind.Rest, FakeEyeCheck.Frames(5, i => (FakeEyeCheck.Open(), FakeEyeCheck.Open()))).Ok);
    }

    [Fact]
    public void Face_missing_fails()
    {
        var frames = FakeEyeCheck.Frames(100, i => (FakeEyeCheck.Open(), FakeEyeCheck.Open()), face: false);
        Assert.Contains("face", Check(EyeCheckStepKind.CloseLeft, frames).Problem);
    }

    [Fact]
    public void Wink_with_the_wrong_eye_fails()
    {
        var frames = FakeEyeCheck.Frames(100, i => (FakeEyeCheck.Open(i), FakeEyeCheck.Closed(i)));
        Assert.Equal("Close your left eye all the way.", Check(EyeCheckStepKind.CloseLeft, frames).Problem);
    }

    [Fact]
    public void Both_closed_when_one_was_asked_fails()
    {
        var frames = FakeEyeCheck.Frames(100, i => (FakeEyeCheck.Closed(i, 0.7), FakeEyeCheck.Closed(i, 0.7)));
        Assert.Contains("Keep your right eye open", Check(EyeCheckStepKind.CloseLeft, frames).Problem);
    }

    [Fact]
    public void Closed_eye_counts_by_iris_ratio_when_blink_scores_are_the_same()
    {
        // tilting the head up makes the blink score of both eyes ~0.37, the iris ratio still shows it
        var sameBlink = FakeEyeCheck.Frames(100, i => (FakeEyeCheck.Closed(i, 0.37), FakeEyeCheck.Open(i) with { Blink = 0.38 }), pitch: -33);
        Assert.True(Check(EyeCheckStepKind.CloseLeftHeadUp, sameBlink).Ok);
    }

    [Fact]
    public void Head_has_to_tilt_the_right_way()
    {
        var notTilted = FakeEyeCheck.Frames(100, i => (FakeEyeCheck.Closed(i), FakeEyeCheck.Open(i)), pitch: -15);
        Assert.Contains("further up", Check(EyeCheckStepKind.CloseLeftHeadUp, notTilted).Problem);

        // tilted down when up was asked
        var wrongWay = FakeEyeCheck.Frames(100, i => (FakeEyeCheck.Closed(i), FakeEyeCheck.Open(i)), pitch: 10);
        Assert.False(Check(EyeCheckStepKind.CloseLeftHeadUp, wrongWay).Ok);

        var down = FakeEyeCheck.Frames(100, i => (FakeEyeCheck.Open(i), FakeEyeCheck.Closed(i)), pitch: -12);
        Assert.Contains("further down", Check(EyeCheckStepKind.CloseRightHeadDown, down).Problem);
    }

    [Fact]
    public void Tilt_is_measured_from_resting()
    {
        // -26 is only 16 up from a resting -10, but that's enough
        var frames = FakeEyeCheck.Frames(100, i => (FakeEyeCheck.Closed(i), FakeEyeCheck.Open(i)), pitch: -26);
        Assert.True(Check(EyeCheckStepKind.CloseLeftHeadUp, frames).Ok);
    }

    [Fact]
    public void Both_eyes_have_to_close()
    {
        var oneOpen = FakeEyeCheck.Frames(100, i => (FakeEyeCheck.Closed(i, 0.7), FakeEyeCheck.Open(i)));
        Assert.False(Check(EyeCheckStepKind.CloseBoth, oneOpen).Ok);
    }

    [Fact]
    public void Look_around_needs_both_eyes_open()
    {
        var closed = FakeEyeCheck.Frames(100, i => (FakeEyeCheck.Closed(i, 0.7), FakeEyeCheck.Open(i)));
        Assert.False(Check(EyeCheckStepKind.LookAround, closed).Ok);
    }

    [Fact]
    public void Live_hint_says_what_to_fix_and_nothing_when_it_is_right()
    {
        var step = FakeEyeCheck.Step(EyeCheckStepKind.CloseLeftHeadUp);
        var right = FakeEyeCheck.Frames(25, i => (FakeEyeCheck.Closed(i), FakeEyeCheck.Open(i)), pitch: -33);
        var notTilted = FakeEyeCheck.Frames(25, i => (FakeEyeCheck.Closed(i), FakeEyeCheck.Open(i)), pitch: -10);

        Assert.Null(EyeCheckSteps.LiveHint(step, right, FakeEyeCheck.Baseline));
        Assert.Contains("further up", EyeCheckSteps.LiveHint(step, notTilted, FakeEyeCheck.Baseline));
        Assert.Null(EyeCheckSteps.LiveHint(step, right.Take(3).ToList(), FakeEyeCheck.Baseline)); // too little to say anything
    }
}
