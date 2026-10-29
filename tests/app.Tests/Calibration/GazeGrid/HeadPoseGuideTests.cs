namespace app.Tests;

public class HeadPoseGuideTests
{
    [Theory]
    // leaning: how far the face moved in the camera image, + = to the person's right
    [InlineData(HeadPose.LeanLeft, -0.03, 0, false, "Lean a little more to the left")]
    [InlineData(HeadPose.LeanLeft, -0.10, 0, true, "Good")]
    [InlineData(HeadPose.LeanLeft, -0.25, 0, false, "A little less")]
    [InlineData(HeadPose.LeanLeft, 0.10, 0, false, "Lean a little more to the left")]   // leaned the wrong way
    [InlineData(HeadPose.LeanRight, 0.12, 0, true, "Good")]
    // tilting: degrees of pitch from sitting normally, more negative is up
    [InlineData(HeadPose.HeadUp, 0, -3, false, "Tilt your head a little more up")]
    [InlineData(HeadPose.HeadUp, 0, -9, true, "Good")]
    [InlineData(HeadPose.HeadUp, 0, -14, false, "A little less")]                    // what "a little" turned into the first time
    [InlineData(HeadPose.HeadDown, 0, 8, true, "Good")]
    [InlineData(HeadPose.HeadDown, 0, 20, false, "A little less")]
    [InlineData(HeadPose.HeadDown, 0, -8, false, "Tilt your head a little more down")]
    public void Guides_to_a_measured_amount(string pose, double moved, double tilted, bool inRange, string hint)
    {
        var (ok, text) = HeadPose.Guide(pose, moved, tilted);
        Assert.Equal(inRange, ok);
        Assert.StartsWith(hint, text);
    }

    [Fact]
    public void Says_how_far_it_is_now()
    {
        Assert.Equal("Tilt your head a little more up (3° of 6°)", HeadPose.Guide(HeadPose.HeadUp, 0, -3).Hint);
        Assert.Equal("Good, hold it there (9°)", HeadPose.Guide(HeadPose.HeadUp, 0, -9).Hint);
        Assert.Equal("Lean a little more to the left (3% of 7%)", HeadPose.Guide(HeadPose.LeanLeft, -0.03, 0).Hint);
        Assert.Equal("A little less, not so far (25%, at most 16%)", HeadPose.Guide(HeadPose.LeanLeft, -0.25, 0).Hint);
        // out loud without the numbers
        Assert.Equal("Tilt your head a little more up", HeadPose.Spoken(HeadPose.Guide(HeadPose.HeadUp, 0, -3).Hint));
    }
}
