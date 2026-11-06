namespace app.Tests;

public class CalibrationSessionTests
{
    [Fact]
    public async Task First_failure_starts_full_recalibration_without_saving()
    {
        var session = new CalibrationSession();
        session.StartFullCalibration();

        session.SetTargets([InvalidTarget(3)]);

        var fullRestarted = false;
        var saved = false;

        var result = await session.AcceptAsync(startFullCalibration: () =>
            {
                fullRestarted = true;
                return Task.CompletedTask;
            },
            retryTargets: _ => Task.CompletedTask,
            showFailure: () => Task.CompletedTask,
            save: () =>
            {
                saved = true;
                return Task.CompletedTask;
            },
            showPreview: () => Task.CompletedTask);
        Assert.Equal(CalibrationRecoveryAction.FullCalibration, result.Action);
        Assert.True(fullRestarted);
        Assert.False(saved);
        Assert.False(result.Saved);
    }

    [Fact]
    public async Task Second_failure_retries_only_failed_targets()
    {
        var session = new CalibrationSession();
        session.StartFullCalibration();
        session.SetTargets([InvalidTarget(3)]);
        await session.AcceptAsync(
            () =>
            {
                session.StartFullCalibration();
                session.SetTargets([InvalidTarget(3)]);
                return Task.CompletedTask;
            },
            _ => Task.CompletedTask,
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            () => Task.CompletedTask);

        var retried = Array.Empty<int>();

        var result = await session.AcceptAsync(
            () => Task.CompletedTask,
            indexes =>
            {
                retried = indexes.ToArray();
                return Task.CompletedTask;
            },
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            () => Task.CompletedTask);
        Assert.Equal(CalibrationRecoveryAction.RetryTargets, result.Action);
        Assert.Equal([3], retried);
        Assert.False(result.Saved);
    }

    [Fact]
    public async Task Third_failure_cancels_without_saving_or_preview()
    {
        var session = new CalibrationSession();
        session.StartFullCalibration();
        session.SetTargets([InvalidTarget(3)]);

        await session.AcceptAsync(
            () =>
            {
                session.StartFullCalibration();
                session.SetTargets([InvalidTarget(3)]);
                return Task.CompletedTask;
            },
            _ => Task.CompletedTask,
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            () => Task.CompletedTask);
        await session.AcceptAsync(
            () => Task.CompletedTask,
            _ => Task.CompletedTask,
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            () => Task.CompletedTask);
        var failureShown = false;
        var saved = false;
        var previewShown = false;

        var result = await session.AcceptAsync(
            () => Task.CompletedTask,
            _ => Task.CompletedTask,
            () =>
            {
                failureShown = true;
                return Task.CompletedTask;
            },
            () =>
            {
                saved = true;
                return Task.CompletedTask;
            },
            () =>
            {
                previewShown = true;
                return Task.CompletedTask;
            });

        Assert.Equal(CalibrationRecoveryAction.Fail, result.Action);
        Assert.True(failureShown);
        Assert.False(saved);
        Assert.False(previewShown);
    }

    [Fact]
    public async Task Starting_again_by_choice_is_a_fresh_start()
    {
        var session = new CalibrationSession();
        session.StartFullCalibration();
        session.StartFullCalibration();
        session.StartFullCalibration();
        session.SetTargets([InvalidTarget(3)]);

        var result = await session.AcceptAsync(
            () => Task.CompletedTask,
            _ => Task.CompletedTask,
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            () => Task.CompletedTask);

        Assert.Equal(CalibrationRecoveryAction.FullCalibration, result.Action);
    }

    [Fact]
    public void Missing_expected_targets_fail_the_check()
    {
        var session = new CalibrationSession([3, 4]);
        session.StartFullCalibration();
        session.SetTargets([ValidTarget(3)]);

        Assert.Equal([4], session.Validate().Missing.Select(m => m.TargetIndex));
    }

    [Fact]
    public void Next_recovery_says_what_accepting_would_do()
    {
        var session = new CalibrationSession();
        session.StartFullCalibration();
        session.SetTargets([InvalidTarget(3)]);

        Assert.Equal(CalibrationRecoveryAction.FullCalibration, session.NextRecovery().Action);
    }

    private static GazeTargetSummary ValidTarget(int index) =>
        new()
        {
            Index = index,
            X = 0.5,
            Y = 0.5,
            UsableFrames = GazeFit.MinUsableFrames,
            LeftH = new Spread(0.1, 0.1, 0.1, 0.1, 0.1),
            RightH = new Spread(0.2, 0.2, 0.2, 0.2, 0.2),
            Openness = new Spread(0.3, 0.3, 0.3, 0.3, 0.3),
        };

    private static GazeTargetSummary InvalidTarget(int index) =>
        new()
        {
            Index = index,
            X = 0.5,
            Y= 0.5,
            UsableFrames = GazeFit.MinUsableFrames - 1,
        };
}