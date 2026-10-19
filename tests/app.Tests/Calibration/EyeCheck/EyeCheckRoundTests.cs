namespace app.Tests;

public class EyeCheckRoundTests
{
    [Fact]
    public void Switching_eyes_says_to_open_the_other_one()
    {
        Assert.Equal("Open your left eye, and close only your right eye.", EyeCheckRound.Say(CheckTrial.CloseRight, CheckTrial.CloseLeft));
        Assert.Equal("Open your right eye, and close only your left eye.", EyeCheckRound.Say(CheckTrial.CloseLeft, CheckTrial.CloseRight));
        Assert.Equal("Close only your left eye.", EyeCheckRound.Say(CheckTrial.CloseLeft, CheckTrial.OpenBoth));
        Assert.Equal("Close only your right eye.", EyeCheckRound.Say(CheckTrial.CloseRight));
        Assert.Equal("Open both eyes.", EyeCheckRound.Say(CheckTrial.OpenBoth, CheckTrial.CloseLeft));
    }

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
        Assert.True(EyeCheckRound.Matches(CheckTrial.CloseLeft, F(FakeEyeCheck.Closed(), FakeEyeCheck.Open())));
        Assert.False(EyeCheckRound.Matches(CheckTrial.CloseLeft, F(FakeEyeCheck.Closed(), FakeEyeCheck.Closed())));
        Assert.True(EyeCheckRound.Matches(CheckTrial.CloseRight, F(FakeEyeCheck.Open(), FakeEyeCheck.Closed())));
        Assert.True(EyeCheckRound.Matches(CheckTrial.OpenBoth, F(FakeEyeCheck.Open(), FakeEyeCheck.Open())));
        Assert.False(EyeCheckRound.Matches(CheckTrial.OpenBoth, F(FakeEyeCheck.Open(), null)));
        // nothing was in front of the eye, so the tracker calling it covered is wrong
        Assert.False(EyeCheckRound.Matches(CheckTrial.OpenBoth, F(FakeEyeCheck.Open() with { Covered = true }, FakeEyeCheck.Open())));
        Assert.False(EyeCheckRound.Matches(CheckTrial.CloseLeft, F(FakeEyeCheck.Closed() with { Covered = true }, FakeEyeCheck.Open())));
        Assert.False(EyeCheckRound.Matches(CheckTrial.OpenBoth, new FrameSample(2000, false, 0, 0, null, null, 30)));
    }

    [Fact]
    public void Score_and_pass()
    {
        var good = FakeEyeCheck.Frames(60, i => (FakeEyeCheck.Closed(i), FakeEyeCheck.Open(i)));
        // 85% right
        var someMissed = FakeEyeCheck.Frames(60, i => i % 20 < 17 ? (FakeEyeCheck.Open(i), FakeEyeCheck.Open(i)) : (FakeEyeCheck.Closed(i), FakeEyeCheck.Open(i)));

        var scores = EyeCheckRound.Score([(CheckTrial.CloseLeft, good), (CheckTrial.OpenBoth, someMissed)]);

        Assert.Equal(1.0, scores[CheckTrial.CloseLeft]);
        Assert.Equal(0.85, scores[CheckTrial.OpenBoth], 2);
        Assert.False(EyeCheckRound.Passed(scores));
        Assert.True(EyeCheckRound.Passed(new Dictionary<CheckTrial, double> { [CheckTrial.CloseLeft] = 0.95 }));
        Assert.False(EyeCheckRound.Passed(new Dictionary<CheckTrial, double>()));
    }
}
