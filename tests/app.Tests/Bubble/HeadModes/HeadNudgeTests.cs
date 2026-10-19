namespace app.Tests;

public class HeadNudgeTests
{
    private static readonly HeadSettings Settings = new()
    {
        Mode = HeadMode.Both, NudgeStrength = 1.5, NudgeDeadZone = 1, NudgeSettleMs = 300, NudgeReleasePercent = 15,
    };

    // the eyes on one spot for long enough to settle, at 60 fps, head at yaw 2, pitch -12. returns the next ms
    private static long Settle(HeadNudge nudge, double x, double y, long from = 0, double yaw = 2, double pitch = -12)
    {
        long ms = from;
        for (int i = 0; i < 25; i++, ms += 16) nudge.Update(ms, x, y, yaw, pitch);
        return ms;
    }

    [Fact]
    public void FollowsTheEyes_UntilTheySettle()
    {
        var nudge = new HeadNudge(Settings);
        var (x, y) = nudge.Update(0, 0.3, 0.6, 2, -12);
        Assert.Equal((0.3, 0.6), (x, y));
        Assert.False(nudge.Holding);

        Settle(nudge, 0.3, 0.6);
        Assert.True(nudge.Holding);
    }

    [Fact]
    public void MovingEyes_DontSettle()
    {
        var nudge = new HeadNudge(Settings);
        for (int i = 0; i < 40; i++) nudge.Update(i * 16, 0.1 + i * 0.02, 0.5, 2, -12);
        Assert.False(nudge.Holding);
    }

    [Fact]
    public void TurningRightAndTiltingDown_MovesItRightAndDown()
    {
        var nudge = new HeadNudge(Settings);
        long ms = Settle(nudge, 0.1, 0.85);

        // 5° to the right and 4° down, held long enough for the smoothing to catch up
        (double X, double Y) at = default;
        for (int i = 0; i < 30; i++, ms += 16) at = nudge.Update(ms, 0.1, 0.85, 7, -8);

        // past the 1° dead zone: 4° and 3°, at 1.5% a degree
        Assert.Equal(0.1 + 0.06, at.X, 3);
        Assert.Equal(0.85 + 0.045, at.Y, 3);
    }

    [Fact]
    public void TurningLeftAndTiltingUp_MovesItLeftAndUp()
    {
        var nudge = new HeadNudge(Settings);
        long ms = Settle(nudge, 0.5, 0.5);
        (double X, double Y) at = default;
        for (int i = 0; i < 30; i++, ms += 16) at = nudge.Update(ms, 0.5, 0.5, -3, -18);
        Assert.True(at.X < 0.5);
        Assert.True(at.Y < 0.5);
    }

    [Fact]
    public void SmallHeadMovement_InTheDeadZone_DoesNothing()
    {
        var nudge = new HeadNudge(Settings);
        long ms = Settle(nudge, 0.4, 0.4);
        (double X, double Y) at = default;
        for (int i = 0; i < 30; i++, ms += 16) at = nudge.Update(ms, 0.4, 0.4, 2.8, -11.3);
        Assert.Equal((0.4, 0.4), at);
    }

    [Fact]
    public void Nudge_StopsAtItsLimit()
    {
        var nudge = new HeadNudge(Settings);
        long ms = Settle(nudge, 0.5, 0.5);
        for (int i = 0; i < 60; i++, ms += 16) nudge.Update(ms, 0.5, 0.5, 60, -12);
        Assert.Equal(HeadNudge.MaxNudge, nudge.Nudge.X, 6);
    }

    [Fact]
    public void LookingFarAway_LetsGo_ButOneBadFrameDoesnt()
    {
        var nudge = new HeadNudge(Settings);
        long ms = Settle(nudge, 0.2, 0.2);

        nudge.Update(ms, 0.9, 0.9, 2, -12);
        nudge.Update(ms + 16, 0.2, 0.2, 2, -12);
        Assert.True(nudge.Holding);

        ms += 32;
        (double X, double Y) at = default;
        for (int i = 0; i < 12; i++, ms += 16) at = nudge.Update(ms, 0.8, 0.7, 2, -12);
        Assert.False(nudge.Holding);
        Assert.Equal((0.8, 0.7), at);
    }

    [Fact]
    public void LookingAtTheNudgedBubble_KeepsHolding()
    {
        // held at 0.3, nudged 25% to the right with the head, then the eyes go to the bubble: 25% from the held spot is
        // past the let-go distance (15%), but it's where the bubble is, so it keeps holding
        var nudge = new HeadNudge(Settings);
        long ms = Settle(nudge, 0.3, 0.5);
        (double X, double Y) at = default;
        for (int i = 0; i < 40; i++, ms += 16) at = nudge.Update(ms, 0.3, 0.5, 2 + 17.67, -12);
        Assert.Equal(0.55, at.X, 2);

        for (int i = 0; i < 30; i++, ms += 16) at = nudge.Update(ms, at.X, 0.5, 2 + 17.67, -12);
        Assert.True(nudge.Holding);
        Assert.Equal(0.55, at.X, 2);
    }

    [Fact]
    public void Reset_GoesBackToFollowing()
    {
        var nudge = new HeadNudge(Settings);
        Settle(nudge, 0.5, 0.5);
        nudge.Reset();
        Assert.False(nudge.Holding);
        Assert.Equal((0.0, 0.0), nudge.Nudge);
    }
}
