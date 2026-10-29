namespace app.Tests;

public class PositionGuideTests
{
    private long _now;
    private static readonly HeadPosition Calibrated = new(0.5, 0.4, 0.21, -15);

    private static FrameSample At(double faceX, double faceY = 0.4, double faceW = 0.21, double pitch = -15) =>
        new(0, true, 0, pitch, null, null, 30, 0, faceX, faceY, faceW);

    private void Feed(PositionGuide g, FrameSample f, long ms)
    {
        for (long t = 0; t <= ms; t += 50)
        {
            g.OnFrame(f);
            _now += 50;
        }
    }

    private PositionGuide Make() => new(() => _now) { Calibrated = Calibrated };

    [Theory]
    [InlineData(0.65, 0.4, 0.21, "Move a little to your left")]       // face moved to the person's right in the image
    [InlineData(0.35, 0.4, 0.21, "Move a little to your right")]
    [InlineData(0.5, 0.55, 0.21, "Sit up a little")]                  // slid down
    [InlineData(0.5, 0.25, 0.21, "Sit a little lower")]
    [InlineData(0.5, 0.4, 0.27, "Move back a little")]                // face bigger: closer
    [InlineData(0.5, 0.4, 0.16, "Move a little closer")]
    public void Says_which_way_to_go(double x, double y, double w, string start)
    {
        Assert.StartsWith(start, PositionGuide.MoveFrom(Calibrated, new HeadPosition(x, y, w, -15)));
    }

    [Fact]
    public void Close_to_calibration_says_nothing_and_the_biggest_problem_wins()
    {
        Assert.Null(PositionGuide.MoveFrom(Calibrated, new HeadPosition(0.55, 0.43, 0.22, -12)));
        // 0.25 to the side is worse than slightly too close
        Assert.StartsWith("Move a little to your left", PositionGuide.MoveFrom(Calibrated, new HeadPosition(0.75, 0.4, 0.25, -15)));
    }

    [Fact]
    public void Move_hint_only_after_it_lasts_and_goes_away_after_moving_back()
    {
        var g = Make();
        Feed(g, At(0.7), 700);
        Assert.Null(g.MoveHint);                         // not yet, could be passing through
        Feed(g, At(0.7), 500);
        Assert.StartsWith("Move a little to your left", g.MoveHint);

        Feed(g, At(0.555), 1600);                        // still past 0.8 of the limit (6%): stays
        Assert.NotNull(g.MoveHint);
        Feed(g, At(0.53), 1600);                         // the position is the median of the last 1.5 s
        Assert.Null(g.MoveHint);
    }

    [Fact]
    public void Suggests_a_re_center_after_moving_a_bit_for_a_while()
    {
        // 5.5% to the side: past the re-center's 5%, not the move hint's 6%
        var g = Make();
        Feed(g, At(0.555), 6000);
        Assert.False(g.SuggestRecenter);
        Feed(g, At(0.555), 3000);
        Assert.True(g.SuggestRecenter);
        Assert.Null(g.MoveHint);

        // re-centering there takes it away, and it doesn't come back staying there
        g.Recentered(new HeadPosition(0.555, 0.4, 0.21, -15));
        Assert.False(g.SuggestRecenter);
        Feed(g, At(0.555), 12000);
        Assert.False(g.SuggestRecenter);
    }

    [Fact]
    public void A_clearly_tilted_head_says_to_tilt_it_back()
    {
        // the tracker's pitch goes up tilting down: 10 degrees down from the calibration
        var g = Make();
        Feed(g, At(0.5, pitch: -5), 1500);
        Assert.Equal("Tilt your head up a little", g.MoveHint);
        g = Make();
        Feed(g, At(0.5, pitch: -25), 1500);
        Assert.Equal("Tilt your head down a little", g.MoveHint);
    }

    [Fact]
    public void Hints_follow_the_settings()
    {
        // 4% to the side: nothing at the default 6%, a hint at 3%
        Assert.Null(PositionGuide.MoveFrom(Calibrated, new HeadPosition(0.54, 0.4, 0.21, -15)));
        Assert.NotNull(PositionGuide.MoveFrom(Calibrated, new HeadPosition(0.54, 0.4, 0.21, -15), moveAt: 0.03));
        // 6 degrees of tilt: nothing at the default 8, a hint at 5
        Assert.Null(PositionGuide.MoveFrom(Calibrated, new HeadPosition(0.5, 0.4, 0.21, -9)));
        Assert.NotNull(PositionGuide.MoveFrom(Calibrated, new HeadPosition(0.5, 0.4, 0.21, -9), tiltAt: 5));
    }

    [Fact]
    public void Pitch_jitter_and_reading_the_bottom_of_the_screen_dont()
    {
        // what a real calibration showed sitting still: pitch jumping by up to 4°, and 3-4° more looking at the bottom
        var g = Make();
        for (int i = 0; i < 400; i++)
        {
            g.OnFrame(At(0.5 + (i % 3) * 0.001, pitch: -15 + (i % 2 == 0 ? 4 : -2) + (i > 200 ? 3.5 : 0)));
            _now += 50;
        }
        Assert.False(g.SuggestRecenter);
        Assert.Null(g.MoveHint);
    }

    [Fact]
    public void Nothing_without_a_calibration_or_a_face()
    {
        var g = new PositionGuide(() => _now);
        Feed(g, At(0.9), 5000);
        Assert.Null(g.MoveHint);

        g.Calibrated = Calibrated;
        Feed(g, new FrameSample(0, false, 0, 0, null, null, 30), 5000);
        Assert.Null(g.MoveHint);
    }

    [Fact]
    public void Calibrated_position_comes_from_the_nine_targets()
    {
        var targets = Enumerable.Range(0, 9).Select(i => new GazeTargetSummary
        {
            Index = i, FaceX = new Spread(0, 0, 0.5 + i * 0.001, 0, 0), FaceY = new Spread(0, 0, 0.4, 0, 0), FaceW = 0.21,
            Pitch = new Spread(0, 0, -15, 0, 0),
        }).Append(new GazeTargetSummary { Index = 20, Pose = HeadPose.LeanLeft, FaceX = new Spread(0, 0, 0.3, 0, 0) }).ToList();

        var at = HeadPosition.Of(new CalibrationProfile { GazeTargets = targets })!;

        Assert.Equal(0.504, at.FaceX, 6);   // the lean dot isn't counted
        Assert.Equal(0.4, at.FaceY);
        Assert.Equal(-15, at.Pitch);
        Assert.Null(HeadPosition.Of(new CalibrationProfile()));
    }
}
