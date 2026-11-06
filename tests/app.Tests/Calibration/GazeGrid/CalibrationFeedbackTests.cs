namespace app.Tests;

public class CalibrationFeedbackTests
{
    private static readonly EyeSample ClosedEye = new(0.9, 0.4, 0.9, 0.1, 60, true, false);
    private static readonly EyeSample CoveredEye = new(0.9, 0.4, 0.1, 0.3, 60, false, true);

    private static MissingCalibrationFeature Missing(int index, double x = 0.1, double y = 0.1, string? pose = null) =>
        new(index, x, y, pose, "UsableFrames (0/10)");

    private static string Explain(List<FrameSample> frames, MissingCalibrationFeature? missing = null) =>
        CalibrationFeedback.Explain(new CalibrationFeatureValidation([missing ?? Missing(3)]),
            new Dictionary<int, IReadOnlyList<FrameSample>> { [3] = frames });

    private static List<FrameSample> Frames(int count, EyeSample? left, EyeSample? right, bool face = true) =>
        FakeFrames.Many(count, i => FakeFrames.Frame(i * 33, left, right, face: face));

    [Fact]
    public void Says_when_the_camera_sent_too_little()
    {
        Assert.Contains("camera sent too few frames", Explain(Frames(GazeFit.MinUsableFrames - 1, FakeFrames.Open(), FakeFrames.Open())));
    }

    [Fact]
    public void Says_when_the_face_went_out_of_view()
    {
        Assert.Contains("face went out of view", Explain(Frames(40, null, null, face: false)));
    }

    [Fact]
    public void Says_when_the_eyes_werent_found()
    {
        Assert.Contains("eyes couldn't be found", Explain(Frames(40, null, FakeFrames.Open())));
    }

    [Fact]
    public void Says_when_the_eyes_looked_covered()
    {
        Assert.Contains("cover your eyes", Explain(Frames(40, CoveredEye, FakeFrames.Open())));
    }

    [Fact]
    public void Says_when_the_eyes_looked_closed()
    {
        Assert.Contains("eyes looked closed", Explain(Frames(40, ClosedEye, ClosedEye)));
    }

    [Fact]
    public void Says_where_without_internal_names()
    {
        string text = CalibrationFeedback.Explain(
            new CalibrationFeatureValidation([Missing(1), Missing(2, x: 0.9, y: 0.9, pose: HeadPose.LeanLeft)]),
            new Dictionary<int, IReadOnlyList<FrameSample>> { [1] = Frames(40, ClosedEye, ClosedEye), [2] = Frames(40, ClosedEye, ClosedEye) });

        Assert.Contains("2 dots: top left, bottom right leaning left", text);
        Assert.DoesNotContain("UsableFrames", text);
        Assert.DoesNotContain("Target", text);
    }

    [Fact]
    public void One_dot_with_two_problems_is_counted_once()
    {
        string text = CalibrationFeedback.Explain(
            new CalibrationFeatureValidation([Missing(3), new MissingCalibrationFeature(3, 0.1, 0.1, null, "LeftH")]),
            new Dictionary<int, IReadOnlyList<FrameSample>> { [3] = Frames(40, ClosedEye, ClosedEye) });

        Assert.Contains("1 dot:", text);
    }

    [Fact]
    public void A_target_that_never_ran_says_so()
    {
        var validation = CalibrationFeatureValidator.Validate([], [7]);
        string text = CalibrationFeedback.Explain(validation, new Dictionary<int, IReadOnlyList<FrameSample>>());

        Assert.Contains("camera sent too few frames", text);
        Assert.Contains("not shown", text);
    }

    private static Spread Same(double v) => new(v, v, v, v, v);

    private static GazeTargetSummary Dot(string? pose, double faceX, double pitch) =>
        new() { Pose = pose, FaceX = Same(faceX), Pitch = Same(pitch) };

    private static List<GazeTargetSummary> HeadStep(double leanRight) =>
    [
        Dot(null, 0.5, -15),
        Dot(HeadPose.LeanLeft, 0.4, -15),
        Dot(HeadPose.LeanRight, 0.5 + leanRight, -15),
        Dot(HeadPose.HeadUp, 0.5, -24),
        Dot(HeadPose.HeadDown, 0.5, -6),
    ];

    [Fact]
    public void No_head_note_when_every_position_was_reached()
    {
        Assert.Null(CalibrationFeedback.HeadNote(HeadStep(leanRight: 0.1)));
    }

    [Fact]
    public void Head_note_names_the_position_that_wasnt_reached()
    {
        string note = CalibrationFeedback.HeadNote(HeadStep(leanRight: 0.01))!;
        Assert.Contains("leaning right", note);
        Assert.DoesNotContain("leaning left", note);
        Assert.Contains("still use", note);
    }

    [Fact]
    public void No_head_note_without_a_resting_position_to_compare_with()
    {
        Assert.Null(CalibrationFeedback.HeadNote([Dot(HeadPose.LeanLeft, 0.5, -15)]));
    }

    [Theory]
    [InlineData(HeadPose.LeanLeft, -HeadPose.LeanMin, 0, true)]
    [InlineData(HeadPose.LeanLeft, HeadPose.LeanMin, 0, false)]
    [InlineData(HeadPose.LeanRight, HeadPose.LeanMin, 0, true)]
    [InlineData(HeadPose.HeadUp, 0, -HeadPose.TiltMin, true)]
    [InlineData(HeadPose.HeadUp, 0, HeadPose.TiltMin, false)]
    [InlineData(HeadPose.HeadDown, 0, HeadPose.TiltMin, true)]
    public void Reached_agrees_with_the_live_guide(string pose, double moved, double tilted, bool expected)
    {
        Assert.Equal(expected, HeadPose.Reached(pose, moved, tilted));
        Assert.Equal(expected, HeadPose.Guide(pose, moved, tilted).InRange);
    }

    [Fact]
    public void Targets_that_never_ran_fail_validation()
    {
        Assert.False(CalibrationFeatureValidator.Validate([], [0, 1]).IsValid);
        var result = CalibrationFeatureValidator.Validate([], [4]);
        Assert.Equal(4, Assert.Single(result.Missing).TargetIndex);
    }

    [Fact]
    public void All_36_targets_are_expected_from_a_run()
    {
        Assert.Equal(36, GridWindow.AllTargetIndexes.Count);
        Assert.Equal(36, GridWindow.AllTargetIndexes.Distinct().Count());
    }
}
