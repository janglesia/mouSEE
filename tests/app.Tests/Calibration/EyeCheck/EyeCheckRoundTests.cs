namespace app.Tests;

public class EyeCheckRoundTests
{
    [Fact]
    public void Says_what_to_do()
    {
        Assert.Equal("Close both eyes until you hear the sound.", EyeCheckRound.Say(CheckTrial.CloseBoth));
        Assert.Equal("Open your eyes.", EyeCheckRound.Say(CheckTrial.OpenBoth));
    }

    [Fact]
    public void Closed_and_open_take_turns()
    {
        for (int seed = 0; seed < 20; seed++)
        {
            var order = EyeCheckRound.Order(new Random(seed));
            Assert.Equal(2 * EyeCheckRound.Repeats, order.Count);
            Assert.All(Enum.GetValues<CheckTrial>(), t => Assert.Equal(EyeCheckRound.Repeats, order.Count(o => o == t)));
            Assert.DoesNotContain(order.Zip(order.Skip(1)), p => p.First == p.Second);
        }
        // and it doesn't always start the same way
        Assert.Equal(2, Enumerable.Range(0, 20).Select(seed => EyeCheckRound.Order(new Random(seed))[0]).Distinct().Count());
    }

    [Fact]
    public void Matches_uses_the_tracker_decisions()
    {
        FrameSample F(EyeSample? l, EyeSample? r) => new(2000, true, 0, -10, l, r, 30);
        Assert.True(EyeCheckRound.Matches(CheckTrial.CloseBoth, F(FakeEyeCheck.Closed(), FakeEyeCheck.Closed())));
        // one eye still open is a wink, not closed eyes
        Assert.False(EyeCheckRound.Matches(CheckTrial.CloseBoth, F(FakeEyeCheck.Closed(), FakeEyeCheck.Open())));
        Assert.True(EyeCheckRound.Matches(CheckTrial.OpenBoth, F(FakeEyeCheck.Open(), FakeEyeCheck.Open())));
        Assert.False(EyeCheckRound.Matches(CheckTrial.OpenBoth, F(FakeEyeCheck.Open(), FakeEyeCheck.Closed())));
        Assert.False(EyeCheckRound.Matches(CheckTrial.OpenBoth, F(FakeEyeCheck.Open(), null)));
        // nothing was in front of the eye, so the tracker calling it covered is wrong
        Assert.False(EyeCheckRound.Matches(CheckTrial.OpenBoth, F(FakeEyeCheck.Open() with { Covered = true }, FakeEyeCheck.Open())));
        Assert.False(EyeCheckRound.Matches(CheckTrial.CloseBoth, F(FakeEyeCheck.Closed() with { Covered = true }, FakeEyeCheck.Closed())));
        Assert.False(EyeCheckRound.Matches(CheckTrial.OpenBoth, new FrameSample(2000, false, 0, 0, null, null, 30)));
    }

    [Fact]
    public void Score_and_pass()
    {
        var good = FakeEyeCheck.Frames(60, i => (FakeEyeCheck.Closed(i), FakeEyeCheck.Closed(i)));
        // 85% right
        var someMissed = FakeEyeCheck.Frames(60, i => i % 20 < 17 ? (FakeEyeCheck.Open(i), FakeEyeCheck.Open(i)) : (FakeEyeCheck.Closed(i), FakeEyeCheck.Open(i)));

        var scores = EyeCheckRound.Score([(CheckTrial.CloseBoth, good), (CheckTrial.OpenBoth, someMissed)]);

        Assert.Equal(1.0, scores[CheckTrial.CloseBoth]);
        Assert.Equal(0.85, scores[CheckTrial.OpenBoth], 2);
        Assert.False(EyeCheckRound.Passed(scores));
        Assert.True(EyeCheckRound.Passed(new Dictionary<CheckTrial, double> { [CheckTrial.CloseBoth] = 0.95 }));
        Assert.False(EyeCheckRound.Passed(new Dictionary<CheckTrial, double>()));
    }

    [Fact]
    public void Says_good_while_the_tracker_sees_what_was_asked()
    {
        var closed = FakeEyeCheck.Frames(10, i => (FakeEyeCheck.Closed(i), FakeEyeCheck.Closed(i)));
        Assert.Equal("Good, both eyes closed", EyeCheckRound.LiveGood(CheckTrial.CloseBoth, closed));
        Assert.Null(EyeCheckRound.LiveGood(CheckTrial.OpenBoth, closed));
        Assert.Null(EyeCheckRound.LiveGood(CheckTrial.CloseBoth, closed.Take(3).ToList()));
        Assert.Equal("Good, both eyes open",
                     EyeCheckRound.LiveGood(CheckTrial.OpenBoth, FakeEyeCheck.Frames(10, i => (FakeEyeCheck.Open(i), FakeEyeCheck.Open(i)))));
    }
}
