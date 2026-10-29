namespace app.Tests;

public class LookChoiceTests
{
    private long _now;
    private readonly List<bool> _chosen = [];

    private LookChoice Make()
    {
        var c = new LookChoice(() => _now);
        c.Chosen += yes => _chosen.Add(yes);
        return c;
    }

    // a frame with the eyes looking this far right (+) or left (-), null = eyes closed
    private static TrackerMessage Looking(double? h) => new("frame", 0,
        [new Face([0, 0, 1, 1], [], new Irises([0, 0], [0, 0]), new Pose(0, 0, 0), [],
                  new Eyes(null!, null!, new GazeData(new GazeRaw(h, 0), h, 0, "", new GazeError(null, null), h is null ? 0 : 2)))],
        null, 0, 0);

    private void Hold(LookChoice c, double? h, long ms, long step = 33)
    {
        for (long t = 0; t <= ms; t += step)
        {
            c.OnFrame(Looking(h));
            _now += step;
        }
    }

    [Fact]
    public void Looking_right_for_three_seconds_is_yes()
    {
        var c = Make();
        Hold(c, 0, 200);
        Hold(c, 0.2, 3100);
        Assert.Equal([true], _chosen);
    }

    [Fact]
    public void Looking_left_is_no()
    {
        var c = Make();
        Hold(c, 0, 200);
        Hold(c, -0.2, 3100);
        Assert.Equal([false], _chosen);
    }

    [Fact]
    public void Not_before_the_eyes_were_in_the_middle()
    {
        // already looking right when the question shows up: nothing until they come back to the middle
        var c = Make();
        Hold(c, 0.2, 4000);
        Assert.Empty(_chosen);
        Assert.False(c.Armed);

        Hold(c, 0, 100);
        Hold(c, 0.2, 3100);
        Assert.Equal([true], _chosen);
    }

    [Fact]
    public void Closed_eyes_dont_count_as_the_middle()
    {
        var c = Make();
        Hold(c, null, 500);
        Assert.False(c.Armed);
    }

    [Fact]
    public void Not_held_long_enough_is_nothing()
    {
        var c = Make();
        Hold(c, 0, 100);
        Hold(c, 0.2, 2500);
        Hold(c, 0, 100);
        Assert.Empty(_chosen);
        Assert.Equal(0, c.Progress);
    }

    [Fact]
    public void A_blink_while_holding_doesnt_start_over()
    {
        var c = Make();
        Hold(c, 0, 100);
        Hold(c, 0.2, 1500);
        Hold(c, null, 200); // blink
        Hold(c, 0.2, 1400);
        Assert.Equal([true], _chosen);
    }

    [Fact]
    public void Switching_sides_starts_over()
    {
        var c = Make();
        Hold(c, 0, 100);
        Hold(c, 0.2, 2000);
        Hold(c, -0.2, 2000);
        Assert.Empty(_chosen);
        Assert.Equal(LookSide.Left, c.Side);
    }

    [Fact]
    public void Wobbling_at_the_threshold_keeps_counting()
    {
        // 0.1 isn't enough to start (0.12) but is enough to stay (0.08)
        var c = Make();
        Hold(c, 0, 100);
        Hold(c, 0.13, 500);
        Hold(c, 0.1, 2700);
        Assert.Equal([true], _chosen);
    }

    [Fact]
    public void One_answer_then_back_to_the_middle_for_the_next()
    {
        var c = Make();
        Hold(c, 0, 100);
        Hold(c, 0.2, 6500);
        Assert.Equal([true], _chosen); // not twice

        Hold(c, 0, 100);
        Hold(c, -0.2, 3100);
        Assert.Equal([true, false], _chosen);
    }

    [Fact]
    public void Progress_fills_while_holding()
    {
        var c = Make();
        Hold(c, 0, 100);
        Hold(c, 0.2, 1500);
        Assert.Equal(LookSide.Right, c.Side);
        Assert.InRange(c.Progress, 0.45, 0.55);
    }
}
