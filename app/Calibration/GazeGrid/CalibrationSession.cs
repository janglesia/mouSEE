namespace app.Calibration.GazeGrid;

public sealed record CalibrationSessionResult(CalibrationRecoveryAction Action, IReadOnlyList<int> RetriedTargetIndexes, bool Saved, bool PreviewStarted, bool FailureDisplayed);

public sealed class CalibrationSession
{
    private readonly Dictionary<int, GazeTargetSummary> _targets = [];
    private int _fullCalibrationAttempts;
    private bool _targetRetryAttempted;

    public void StartFullCalibration()
    {
        _fullCalibrationAttempts++;
        _targetRetryAttempted = false;
        _targets.Clear();
    }

    public void SetTargets(IEnumerable<GazeTargetSummary> targets)
    {
        foreach(var target in targets){
            _targets[target.Index] = target;
        }
    }

    public async Task<CalibrationSessionResult> AcceptAsync(
        Func<Task> startFullCalibration,
        Func<IReadOnlyList<int>, Task> retryTargets,
        Func<Task> showFailure,
        Func<Task> save,
        Func<Task> showPreview)
    {
        var validation = CalibrationFeatureValidator.Validate(_targets.Values.ToList());

        if(!validation.IsValid){
            var failedIndexes = validation.Missing.Select(m => m.TargetIndex).Distinct().ToList();

            var decision = CalibrationRecoveryPolicy.Decide(_fullCalibrationAttempts, _targetRetryAttempted, failedIndexes);

            switch(decision.Action)
            {
                case CalibrationRecoveryAction.FullCalibration:
                    await startFullCalibration();
                    return new(CalibrationRecoveryAction.FullCalibration, [], false, false, false);
                case CalibrationRecoveryAction.RetryTargets:
                    _targetRetryAttempted = true;
                    await retryTargets(decision.TargetIndexes);
                    return new(CalibrationRecoveryAction.RetryTargets, decision.TargetIndexes, false, false, false);
                case CalibrationRecoveryAction.Fail:
                    await showFailure();
                    return new(CalibrationRecoveryAction.Fail, [], false, false, true);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        await save();
        await showPreview();

        return new(CalibrationRecoveryAction.None, [], true, true, false);
    }
}