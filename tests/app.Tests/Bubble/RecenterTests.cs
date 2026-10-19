namespace app.Tests;

public class RecenterTests
{
    private static FrameSample Bubble(double? x, double? y, double faceX = 0.5) =>
        new(0, true, 2, -14, null, null, 30, 0, faceX, 0.42, 0.21, x, y);

    [Fact]
    public void Offset_is_how_far_off_the_middle_the_bubble_was()
    {
        var frames = Enumerable.Range(0, 30).Select(i => Bubble(0.45 + (i % 3) * 0.001, 0.58)).ToList();
        var (offset, at, problem) = Recenter.From(frames);

        Assert.Null(problem);
        Assert.Equal(-0.049, offset!.X, 3);
        Assert.Equal(0.08, offset.Y, 3);
        Assert.Equal(0.5, at!.FaceX);
        Assert.Equal(-14, at.Pitch);
    }

    [Fact]
    public void Too_few_frames_with_a_bubble_is_not_used()
    {
        var frames = Enumerable.Range(0, 30).Select(i => i < 10 ? Bubble(0.5, 0.5) : Bubble(null, null)).ToList();
        var (offset, _, problem) = Recenter.From(frames);
        Assert.Null(offset);
        Assert.Contains("both eyes open", problem);
    }

    [Fact]
    public void Very_far_off_needs_a_new_calibration_not_a_re_center()
    {
        var (offset, _, problem) = Recenter.From(Enumerable.Range(0, 30).Select(_ => Bubble(0.9, 0.5)).ToList());
        Assert.Null(offset);
        Assert.Contains("new calibration", problem);
    }
}
