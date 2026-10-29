namespace app.Tests;

public class TrackingMonitorTests
{
    private static readonly Tracking Ok = new("ok", null);
    private static readonly Tracking NoFace = new("lost", "no_face");

    private long _now;

    private TrackingMonitor Make(AdvancedSettings? timings = null, bool paused = false) => new(timings, paused, () => _now);

    private static void Frames(TrackingMonitor m, Tracking t, int count)
    {
        for (int i = 0; i < count; i++) m.OnFrame(t);
    }

    // a monitor that's past Starting/Recovering
    private TrackingMonitor Tracking(AdvancedSettings? settings = null)
    {
        var m = Make(settings);
        Frames(m, Ok, (settings ?? new()).RecoverFrames);
        Assert.Equal(TrackingState.Tracking, m.State);
        return m;
    }

    [Fact]
    public void Recover_frames_come_from_the_settings()
    {
        var m = Make(new AdvancedSettings { RecoverFrames = 3 });
        Frames(m, Ok, 2);
        Assert.Equal(TrackingState.Recovering, m.State);
        m.OnFrame(Ok);
        Assert.Equal(TrackingState.Tracking, m.State);
    }

    [Fact]
    public void Lost_after_comes_from_the_settings()
    {
        var m = Tracking(new AdvancedSettings { LostAfterMs = 200 });
        m.OnFrame(NoFace);
        _now = 199;
        m.OnFrame(NoFace);
        Assert.Equal(TrackingState.Uncertain, m.State);
        _now = 200;
        m.OnFrame(NoFace);
        Assert.Equal(TrackingState.Lost, m.State);
    }

    [Fact]
    public void Not_responding_after_comes_from_the_settings()
    {
        var m = Tracking(new AdvancedSettings { NotRespondingAfterMs = 300 });
        _now = 299;
        m.CheckTimeout();
        Assert.Equal(TrackingState.Tracking, m.State);
        _now = 300;
        m.CheckTimeout();
        Assert.Equal(TrackingState.NotResponding, m.State);
    }

    [Fact]
    public void Configure_changes_timings_while_running()
    {
        var m = Tracking();
        m.Configure(new AdvancedSettings { LostAfterMs = 100 });
        m.OnFrame(NoFace);
        _now = 100;
        m.OnFrame(NoFace);
        Assert.Equal(TrackingState.Lost, m.State);
    }

    [Fact]
    public void Can_start_paused()
    {
        var m = Make(paused: true);
        Assert.Equal(TrackingState.Paused, m.State);
        Assert.False(m.CanMoveCursor);
    }

    [Fact]
    public void Paused_ignores_frames_and_timeouts()
    {
        var m = Tracking();
        m.SetPaused(true);
        Assert.Equal(TrackingState.Paused, m.State);

        Frames(m, NoFace, 5);
        _now = 10_000;
        m.OnFrame(NoFace);
        m.CheckTimeout();

        Assert.Equal(TrackingState.Paused, m.State);
        Assert.False(m.CanMoveCursor);
    }

    [Fact]
    public void Resume_waits_for_good_frames_again()
    {
        var m = Tracking(new AdvancedSettings { RecoverFrames = 3 });
        m.SetPaused(true);
        m.SetPaused(false);

        Assert.Equal(TrackingState.Recovering, m.State);
        Frames(m, Ok, 3);
        Assert.Equal(TrackingState.Tracking, m.State);
    }

    [Fact]
    public void Resume_before_any_frame_is_starting()
    {
        var m = Make(paused: true);
        m.SetPaused(false);
        Assert.Equal(TrackingState.Starting, m.State);
    }

    [Fact]
    public void Unpausing_when_not_paused_does_nothing()
    {
        var m = Tracking();
        m.SetPaused(false);
        Assert.Equal(TrackingState.Tracking, m.State);
    }

    [Fact]
    public void Stopped_stays_stopped()
    {
        var m = Tracking();
        m.OnTrackerStopped("exited");
        m.SetPaused(true);
        Assert.Equal(TrackingState.Stopped, m.State);
    }

    [Fact]
    public void Pausing_fires_changed_once()
    {
        var m = Tracking();
        int changes = 0;
        m.Changed += () => changes++;
        m.SetPaused(true);
        m.SetPaused(true);
        Assert.Equal(1, changes);
    }
}
