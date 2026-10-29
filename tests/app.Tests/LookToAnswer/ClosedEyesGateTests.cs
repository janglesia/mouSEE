namespace app.Tests;

public class ClosedEyesGateTests
{
    private long _now;
    private int _done;

    private ClosedEyesGate Make()
    {
        var g = new ClosedEyesGate(() => _now);
        g.Done += () => _done++;
        return g;
    }

    // a frame with each eye open or closed
    private static TrackerMessage Eyes(bool leftClosed, bool rightClosed) => new("frame", 0,
        [new Face([0, 0, 1, 1], [], new Irises([0, 0], [0, 0]), new Pose(0, 0, 0), [],
                  new Eyes(new EyeData(new EyeOffset(0, 0), leftClosed), new EyeData(new EyeOffset(0, 0), rightClosed),
                           new GazeData(new GazeRaw(0, 0), 0, 0, "", new GazeError(null, null), 2)))],
        null, 0, 0);

    private void Hold(ClosedEyesGate g, bool left, bool right, long ms, long step = 33)
    {
        for (long t = 0; t < ms; t += step)
        {
            g.OnFrame(Eyes(left, right));
            _now += step;
        }
    }

    [Fact]
    public void Both_eyes_closed_long_enough_is_done_once()
    {
        var g = Make();
        Hold(g, true, true, ClosedEyesGate.HoldMs + 100);
        Hold(g, true, true, 2000);
        Assert.Equal(1, _done);
    }

    [Fact]
    public void A_blink_is_not_done()
    {
        var g = Make();
        for (int i = 0; i < 5; i++)
        {
            Hold(g, true, true, 300);
            Hold(g, false, false, 600);
        }
        Assert.Equal(0, _done);
    }

    [Fact]
    public void One_eye_closed_is_not_done()
    {
        var g = Make();
        Hold(g, true, false, 3000);
        Assert.Equal(0, _done);
    }

    [Fact]
    public void An_eye_reading_open_for_a_frame_doesnt_start_over()
    {
        var g = Make();
        Hold(g, true, true, 1000);
        g.OnFrame(Eyes(false, false));
        _now += 33;
        Hold(g, true, true, 600);
        Assert.Equal(1, _done);
    }

    [Fact]
    public void Reset_waits_again()
    {
        var g = Make();
        Hold(g, true, true, 1600);
        g.Reset();
        Hold(g, true, true, 1600);
        Assert.Equal(2, _done);
    }
}
