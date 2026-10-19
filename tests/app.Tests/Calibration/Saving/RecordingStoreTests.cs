using System.IO;

namespace app.Tests;

public sealed class RecordingStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mouSEE-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static CalibrationRecording Sample() => new()
    {
        EyeCheck = [new StepRecording("Rest", 1, true, null, [FakeFrames.Frame(0, FakeFrames.Open(0.1), FakeFrames.Open(0.1)), FakeFrames.Frame(33, null, null, face: false)])],
        CheckRound = [new TrialRecording("OpenBoth", [FakeFrames.Frame(1300, FakeFrames.Open(), FakeFrames.Open())])],
        Gaze = [new TargetRecording(0, 0.08, 0.08, false, [FakeFrames.Frame(0, FakeFrames.Open(-0.2, -0.1), FakeFrames.Open(-0.2, -0.1))])],
    };

    [Fact]
    public void Recording_is_saved_next_to_the_calibration_and_read_back()
    {
        var store = new CalibrationStore(_dir);
        var saved = store.Add(new CalibrationProfile());

        store.SaveRecording(saved.Id, Sample());
        var back = store.LoadRecording(saved.Id)!;

        Assert.Equal(Path.Combine(_dir, "recordings", saved.Id + ".json"), store.RecordingFileFor(saved.Id));
        Assert.Equal(Sample().EyeCheck[0].Frames, back.EyeCheck[0].Frames);
        Assert.Equal(-0.2, back.Gaze[0].Frames[0].Left!.OffsetH);
        Assert.Single(new CalibrationStore(_dir).All); // the recording isn't taken for a calibration
    }

    [Fact]
    public void Deleting_a_calibration_deletes_its_recording()
    {
        var store = new CalibrationStore(_dir);
        var saved = store.Add(new CalibrationProfile());
        store.SaveRecording(saved.Id, Sample());

        store.Delete(saved.Id);

        Assert.False(File.Exists(store.RecordingFileFor(saved.Id)));
    }

    [Fact]
    public void Deleting_one_without_a_recording_works()
    {
        var store = new CalibrationStore(_dir);
        var saved = store.Add(new CalibrationProfile());
        store.Delete(saved.Id);
        Assert.Empty(store.All);
    }

    [Fact]
    public void No_recording_is_null()
    {
        Assert.Null(new CalibrationStore(_dir).LoadRecording("nothing"));
    }

    [Fact]
    public void Report_and_targets_are_in_the_calibration_file()
    {
        var store = new CalibrationStore(_dir);
        store.Add(new CalibrationProfile
        {
            EyeCheck = new EyeCheckReport
            {
                Values = [new EyeValueReport { Name = "MAX_YAW", Used = 35, Default = 35, Measured = new() { ["turnedFurthest"] = 18 } }],
                Steps = [new StepReport { Step = "Rest", Tries = 1, Passed = true }],
                Check = new CheckRoundReport { Passed = true, Scores = new() { ["both eyes open"] = 0.97 } },
            },
            GazeTargets = [new GazeTargetSummary { Index = 4, X = 0.5, Y = 0.5, UsableFrames = 30 }],
        });

        var loaded = new CalibrationStore(_dir).Active!;
        Assert.Equal(18, loaded.EyeCheck!.Values[0].Measured["turnedFurthest"]);
        Assert.Equal(0.97, loaded.EyeCheck.Check!.Scores["both eyes open"]);
        Assert.Equal(4, loaded.GazeTargets[0].Index);
        Assert.Contains("\"turnedFurthest\": 18", File.ReadAllText(store.ActiveFile!));
    }
}
