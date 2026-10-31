namespace app.Calibration.GazeGrid;

public enum CalibrationRecoveryAction
{
    None,
    FullCalibration,
    RetryTargets,
    Fail,
}

public sealed record CalibrationRecoveryDecision(CalibrationRecoveryAction Action, IReadOnlyList<int> TargetIndexes);

public static class CalibrationRecoveryPolicy
{
    public static CalibrationRecoveryDecision Decide(int fullCalibrationAttempts, bool targetRetryAttempted, IReadOnlyList<int> failedTargetIndexes)
    {
        if(fullCalibrationAttempts == 1){
            return new CalibrationRecoveryDecision(CalibrationRecoveryAction.FullCalibration, []);
        }

        if(fullCalibrationAttempts == 2 && !targetRetryAttempted){
            return new CalibrationRecoveryDecision(CalibrationRecoveryAction.RetryTargets, failedTargetIndexes);
        }

        return new CalibrationRecoveryDecision(CalibrationRecoveryAction.Fail, []);
    }
}