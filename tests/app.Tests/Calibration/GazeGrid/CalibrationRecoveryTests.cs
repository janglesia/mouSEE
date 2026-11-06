namespace app.Tests;

public class CalibrationRecoveryTests
{
    [Fact]
    public void First_validation_failure_starts_full_recalibration()
    {
        var decision = CalibrationRecoveryPolicy.Decide(fullCalibrationAttempts: 1, targetRetryAttempted: false, failedTargetIndexes: [3, 7]);

        Assert.Equal(CalibrationRecoveryAction.FullCalibration, decision.Action);
        Assert.Empty(decision.TargetIndexes);
    }

    [Fact]
    public void Second_validation_failure_retries_only_failed_targets()
    {
        var decision = CalibrationRecoveryPolicy.Decide(fullCalibrationAttempts: 2, targetRetryAttempted: false, failedTargetIndexes: [3, 7]);

        Assert.Equal(CalibrationRecoveryAction.RetryTargets, decision.Action);
        Assert.Equal([3, 7], decision.TargetIndexes);
    }

    [Fact]
    public void Third_validation_failure_cancels_calibration()
    {
        var decision = CalibrationRecoveryPolicy.Decide(fullCalibrationAttempts: 2, targetRetryAttempted: true, failedTargetIndexes: [3, 7]);

        Assert.Equal(CalibrationRecoveryAction.Fail, decision.Action);
        Assert.Empty(decision.TargetIndexes);
    }
}