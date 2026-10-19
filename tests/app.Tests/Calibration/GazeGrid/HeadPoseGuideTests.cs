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
}
