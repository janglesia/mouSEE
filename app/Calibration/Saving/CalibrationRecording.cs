namespace app.Calibration.Saving;

// The raw frames of a calibration, in their own file (app/Calibration/Saved/recordings/<id>.json) so the calibration
// file stays readable. Every frame of every try, reaction time included, so the analysis can be run again later.

public sealed record CalibrationRecording
{
    public int Version { get; init; } = 1;
    public List<StepRecording> EyeCheck { get; init; } = [];
    public List<TrialRecording> CheckRound { get; init; } = [];
    public List<TargetRecording> Gaze { get; init; } = [];
}
