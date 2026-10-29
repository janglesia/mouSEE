namespace app.Calibration.Saving;

// The raw frames of a calibration, in their own file (app/Calibration/Saved/recordings/<id>.json) so the calibration
// file stays readable. Every frame of every try, reaction time included, so the analysis can be run again later.

public sealed record CalibrationRecording
{
    // 2: offsetV is measured from the eye corners. 1: from the middle of the lids, which follow the eye, so it says
    // little about up/down and can't be fitted together with the new one (App.FitOldCalibrations leaves it out)
    public const int CurrentVersion = 2;
    public const int CornerOffsetVVersion = 2;

    public int Version { get; init; } = CurrentVersion;
    public List<StepRecording> EyeCheck { get; init; } = [];
    public List<TrialRecording> CheckRound { get; init; } = [];
    public List<TargetRecording> Gaze { get; init; } = [];
}
