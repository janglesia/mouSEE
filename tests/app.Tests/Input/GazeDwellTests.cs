namespace app.Tests;

public class GazeDwellTests
{
    private const int Frame = 16;

    // the same target every frame from start to end (ms), and whether any frame picked it
    private static bool Look(GazeDwell dwell, int? target, long start, long end)
    {
        bool picked = false;
        for (long ms = start; ms <= end; ms += Frame) picked |= dwell.Update(ms, target);
        return picked;
    }

    [Fact]
    public void Looking_long_enough_picks_the_target()
    {
        var dwell = new GazeDwell(900, 150);
        Assert.False(Look(dwell, 3, 0, 880));
        Assert.True(dwell.Progress > 0.9);
        Assert.True(Look(dwell, 3, 896, 920));
    }

    [Fact]
    public void It_picks_only_once()
    {
        var dwell = new GazeDwell(900, 150);
        Assert.True(Look(dwell, 3, 0, 1000));
        Assert.False(Look(dwell, 3, 1016, 3000));
    }

    [Fact]
    public void A_glance_away_doesnt_start_over()
    {
        var dwell = new GazeDwell(900, 150);
        Look(dwell, 3, 0, 500);
        Look(dwell, null, 516, 600);
        Assert.Equal(3, dwell.Target);
        Assert.True(Look(dwell, 3, 616, 920));
    }

    [Fact]
    public void Looking_away_long_enough_starts_over()
    {
        var dwell = new GazeDwell(900, 150);
        Look(dwell, 3, 0, 500);
        Look(dwell, null, 516, 700);
        Assert.Null(dwell.Target);
        Assert.Equal(0, dwell.Progress);
        Assert.False(Look(dwell, 3, 716, 1500));
        Assert.True(Look(dwell, 3, 1516, 1700));
    }

    [Fact]
    public void Moving_to_another_target_starts_its_own_timer()
    {
        var dwell = new GazeDwell(900, 150);
        Look(dwell, 3, 0, 500);
        Look(dwell, 5, 516, 700);
        Assert.Equal(5, dwell.Target);
        // counted from when the gaze got there, not from when it switched
        Assert.False(Look(dwell, 5, 716, 1400));
        Assert.True(Look(dwell, 5, 1416, 1440));
    }

    [Fact]
    public void A_brief_look_at_another_target_doesnt_switch()
    {
        var dwell = new GazeDwell(900, 150);
        Look(dwell, 3, 0, 500);
        Look(dwell, 5, 516, 580);
        Assert.Equal(3, dwell.Target);
    }

    [Fact]
    public void Changed_numbers_take_effect_right_away()
    {
        var dwell = new GazeDwell(900, 150) { DwellMs = 300 };
        Assert.True(Look(dwell, 3, 0, 320));
    }

    [Fact]
    public void A_resting_gaze_picks_its_spot()
    {
        var dwell = new GazeDwell(1000, 150);
        bool picked = false;
        // shaking within the radius
        for (long ms = 0; ms <= 1020; ms += Frame) picked |= dwell.UpdateSpot(ms, 500 + (ms % 3) * 10, 300, 35);
        Assert.True(picked);
        Assert.Equal((500.0, 300.0), dwell.Anchor);
    }

    [Fact]
    public void A_spot_starts_over_where_the_gaze_went()
    {
        var dwell = new GazeDwell(1000, 150);
        for (long ms = 0; ms <= 600; ms += Frame) dwell.UpdateSpot(ms, 500, 300, 35);
        // one stray frame further than the radius doesn't
        dwell.UpdateSpot(616, 600, 300, 35);
        Assert.Equal((500.0, 300.0), dwell.Anchor);
        // staying there does
        for (long ms = 632; ms <= 900; ms += Frame) dwell.UpdateSpot(ms, 900, 700, 35);
        Assert.Equal((900.0, 700.0), dwell.Anchor);
        Assert.True(dwell.Progress < 0.3);
    }

    [Fact]
    public void Reset_forgets_everything()
    {
        var dwell = new GazeDwell(1000, 150);
        for (long ms = 0; ms <= 600; ms += Frame) dwell.UpdateSpot(ms, 500, 300, 35);
        dwell.Reset();
        Assert.Null(dwell.Target);
        Assert.Null(dwell.Anchor);
        Assert.Equal(0, dwell.Progress);
    }

    [Fact]
    public void Wheel_defaults_match_what_the_wheel_did_before()
    {
        var wheel = new WheelSettings();
        Assert.Equal(1000, wheel.OpenDwellMs);
        Assert.Equal(35, wheel.OpenRadiusPx);
        Assert.Equal(900, wheel.SelectDwellMs);
        Assert.Equal(800, wheel.CooldownMs);
    }
}
