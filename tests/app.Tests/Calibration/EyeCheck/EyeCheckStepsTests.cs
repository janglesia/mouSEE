namespace app.Tests;

public class EyeCheckStepsTests
{
    private static StepCheck Check(EyeCheckStepKind kind, List<FrameSample> frames) =>
        EyeCheckSteps.Check(FakeEyeCheck.Step(kind), frames, FakeEyeCheck.Baseline);

    private static List<FrameSample> BothClosed(double pitch = -10) =>
        FakeEyeCheck.Frames(100, i => (FakeEyeCheck.Closed(i), FakeEyeCheck.Closed(i)), pitch: pitch);

    [Fact]
    public void Steps_are_in_the_order_of_the_doc()
    {
        Assert.Equal(Enum.GetValues<EyeCheckStepKind>(), EyeCheckSteps.All.Select(s => s.Kind));
    }

    [Fact]
    public void Tilt_steps_say_to_close_the_eyes_once_the_head_is_tilted()
    {
        // the live guidance for the tilt can't be read with the eyes closed
        foreach (var step in EyeCheckSteps.All)
        {
            Assert.Equal(step.IsTilt, step.Close is not null);
            if (step.IsTilt) Assert.DoesNotContain("close", step.Say, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Reaction_time_is_thrown_away()
    {
        var frames = Enumerable.Range(0, 50).Select(i => new FrameSample(i * 50, true, 0, 0, null, null, 30)).ToList();
        Assert.All(EyeCheckSteps.Usable(frames), f => Assert.True(f.Ms >= 1200));
        Assert.Equal(26, EyeCheckSteps.Usable(frames).Count);
    }

    [Fact]
    public void Resting_angle_is_the_median_of_the_grid()
    {
        var grid = FakeEyeCheck.Frames(100, i => (FakeEyeCheck.Open(i), FakeEyeCheck.Open(i)), pitchAt: i => -14 + i % 3, yaw: 2.5);
        Assert.Equal(new EyeCheckBaseline(-13, 2.5), EyeCheckSteps.Baseline(grid));
        Assert.Null(EyeCheckSteps.Baseline(grid.Take(5).ToList()));
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
        Assert.False(Check(EyeCheckStepKind.CloseBoth, FakeEyeCheck.Frames(5, i => (FakeEyeCheck.Closed(), FakeEyeCheck.Closed()))).Ok);
    }

    [Fact]
    public void Face_missing_fails()
    {
        var frames = FakeEyeCheck.Frames(100, i => (FakeEyeCheck.Closed(), FakeEyeCheck.Closed()), face: false);
        Assert.Contains("face", Check(EyeCheckStepKind.CloseBoth, frames).Problem);
    }

    [Fact]
    public void Both_eyes_have_to_close()
    {
        var oneOpen = FakeEyeCheck.Frames(100, i => (FakeEyeCheck.Closed(i), FakeEyeCheck.Open(i)));
        Assert.Equal("Keep both eyes closed until you hear the sound.", Check(EyeCheckStepKind.CloseBoth, oneOpen).Problem);
    }

    [Fact]
    public void Shut_lids_count_when_the_blink_score_is_low()
    {
        // tilting the head can lower MediaPipe's blink score, the lids still show it
        var lowBlink = FakeEyeCheck.Frames(100, i => (FakeEyeCheck.Closed(i, blink: 0.3), FakeEyeCheck.Closed(i, blink: 0.3)), pitch: -33);
        Assert.True(Check(EyeCheckStepKind.CloseBothHeadUp, lowBlink).Ok);
    }

    [Fact]
    public void Head_has_to_tilt_the_right_way()
    {
        Assert.Contains("further up", Check(EyeCheckStepKind.CloseBothHeadUp, BothClosed(pitch: -15)).Problem);
        // tilted down when up was asked
        Assert.False(Check(EyeCheckStepKind.CloseBothHeadUp, BothClosed(pitch: 10)).Ok);
        Assert.Contains("further down", Check(EyeCheckStepKind.CloseBothHeadDown, BothClosed(pitch: -12)).Problem);
    }

    [Fact]
    public void Tilt_is_measured_from_resting()
    {
        // -26 is only 16 up from a resting -10, but that's enough
        Assert.True(Check(EyeCheckStepKind.CloseBothHeadUp, BothClosed(pitch: -26)).Ok);
    }

    // resting pitch -10 (FakeEyeCheck.Baseline), the tracker's pitch gets more negative tilting up
    private static (bool InRange, string Hint)? Tilt(EyeCheckStepKind kind, double pitch) =>
        EyeCheckSteps.TiltGuide(FakeEyeCheck.Step(kind), FakeEyeCheck.Frames(10, i => (FakeEyeCheck.Open(i), FakeEyeCheck.Open(i)), pitch: pitch),
                                FakeEyeCheck.Baseline);

    [Fact]
    public void Tilt_guide_says_how_far_to_go()
    {
        Assert.Equal((false, "Tilt your head up a little more (10° of 15°)"), Tilt(EyeCheckStepKind.CloseBothHeadUp, -20));
        Assert.Equal((true, "Good, hold it there (20°)"), Tilt(EyeCheckStepKind.CloseBothHeadUp, -30));
        Assert.Equal((false, "A little less, tilt your head back a bit (35°)"), Tilt(EyeCheckStepKind.CloseBothHeadUp, -45));
        Assert.Equal((true, "Good, hold it there (18°)"), Tilt(EyeCheckStepKind.CloseBothHeadDown, 8));
        // tilting the wrong way is "more" of the right way, from nothing
        Assert.Equal((false, "Tilt your head down a little more (0° of 15°)"), Tilt(EyeCheckStepKind.CloseBothHeadDown, -20));
    }

    [Fact]
    public void Tilt_guide_only_for_the_tilt_steps_and_with_a_resting_angle()
    {
        Assert.Null(Tilt(EyeCheckStepKind.CloseBoth, -30));
        Assert.Null(EyeCheckSteps.TiltGuide(FakeEyeCheck.Step(EyeCheckStepKind.CloseBothHeadUp),
                                            FakeEyeCheck.Frames(10, i => (FakeEyeCheck.Open(i), FakeEyeCheck.Open(i)), pitch: -30), null));
    }

    private static (bool Good, string Text)? Live(EyeCheckStepKind kind, bool closed, double pitch = -10, bool problemsToo = true) =>
        EyeCheckSteps.LiveStatus(FakeEyeCheck.Step(kind),
            FakeEyeCheck.Frames(20, i => closed ? (FakeEyeCheck.Closed(i), FakeEyeCheck.Closed(i)) : (FakeEyeCheck.Open(i), FakeEyeCheck.Open(i)), pitch: pitch),
            FakeEyeCheck.Baseline, problemsToo);

    [Fact]
    public void Live_status_says_good_when_the_eyes_are_closed()
    {
        Assert.Equal((true, "Good, keep them closed"), Live(EyeCheckStepKind.CloseBoth, true));
        // the tilt steps say the angle when both the head and the eyes are right
        Assert.Equal((true, "Good, hold it there (20°)"), Live(EyeCheckStepKind.CloseBothHeadUp, true, pitch: -30));
    }

    [Fact]
    public void Live_status_says_whats_wrong_only_once_there_was_time_to_react()
    {
        var wrong = Live(EyeCheckStepKind.CloseBoth, false);
        Assert.False(wrong!.Value.Good);
        Assert.Null(Live(EyeCheckStepKind.CloseBoth, false, problemsToo: false));
        // the head angle is live from the start though
        Assert.Equal((false, "Tilt your head up a little more (5° of 15°)"),
                     Live(EyeCheckStepKind.CloseBothHeadUp, true, pitch: -15, problemsToo: false));
    }
}
