namespace app.Calibration.GazeGrid;

public sealed record CalibrationSessionResult(CalibrationRecoveryAction Action, IReadOnlyList<int> RetriedTargetIndexes, bool Saved, bool PreviewStarted, bool FailureDisplayed);

// expectedTargets: the targets a whole run should have, see CalibrationFeatureValidator
public sealed class CalibrationSession(IReadOnlyCollection<int>? expectedTargets = null)
{
    private readonly Dictionary<int, GazeTargetSummary> _targets = [];
    private int _fullCalibrationAttempts;
    private bool _targetRetryAttempted;
    private bool _automaticRestart;

    // only the restart after a failed check counts as another attempt. Starting again by choice is a fresh start, or a
    // later failure would skip the recovery steps
    public void StartFullCalibration()
    {
        _fullCalibrationAttempts = _automaticRestart ? _fullCalibrationAttempts + 1 : 1;
        _automaticRestart = false;
        _targetRetryAttempted = false;
        _targets.Clear();
    }

    public void SetTargets(IEnumerable<GazeTargetSummary> targets)
    {
        foreach(var target in targets){
            _targets[target.Index] = target;
        }
    }

    public CalibrationFeatureValidation Validate() => CalibrationFeatureValidator.Validate(_targets.Values.ToList(), expectedTargets);

    /// <summary>What accepting would do now, if the check fails.</summary>
    public CalibrationRecoveryDecision NextRecovery() => CalibrationRecoveryPolicy.Decide(_fullCalibrationAttempts, _targetRetryAttempted,
        Validate().Missing.Select(m => m.TargetIndex).Distinct().ToList());

    public async Task<CalibrationSessionResult> AcceptAsync(
        Func<Task> startFullCalibration,
        Func<IReadOnlyList<int>, Task> retryTargets,
        Func<Task> showFailure,
        Func<Task> save,
        Func<Task> showPreview)
    {
        if(!Validate().IsValid){
            var decision = NextRecovery();

            switch(decision.Action)
            {
                case CalibrationRecoveryAction.FullCalibration:
                    // before the call: it can start the run, and with it StartFullCalibration, straight away
                    _automaticRestart = true;
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