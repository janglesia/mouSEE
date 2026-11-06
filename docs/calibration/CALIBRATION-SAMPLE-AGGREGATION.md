# Calibration sample aggregation

## Scope

This work turns the frames captured for each calibration target into one stable
`GazeTargetSummary`. The summary is the input used by the gaze fit and is
validated before the calibration is fitted or saved.

Sample collection remains the responsibility of the sample-collection work.
This implementation consumes the captured frames and does not replace the
tracker or the target capture sequence.

## Completed implementation

### Per-target aggregation

`GazeTargetSummary.Of` in
[`GazeTargetSummary.cs`](../../app/Calibration/GazeGrid/GazeTargetSummary.cs)
aggregates the usable frames for each target. Usable frames require:

- A detected face
- Both eyes present and not covered
- Both eyes open, except for the existing looking-down compatibility rule

The summary stores the total frame count, usable frame count, and the
percentile-based `Spread` values used by the fit, including:

- Left and right horizontal iris offsets
- Left and right vertical iris offsets
- Eye openness
- Face position and width
- Head pitch and yaw
- Averaged vertical iris offset

Raw target recordings are retained separately so the captured data can be
saved and reprocessed.

### Required-feature validation

[`CalibrationFeatureValidation.cs`](../../app/Calibration/GazeGrid/CalibrationFeatureValidation.cs)
defines the validation contract:

- Normal targets require `LeftH`, `RightH`, and `Openness`.
- Pose targets additionally require `FaceX` and `Pitch`.
- Every target must have at least `GazeFit.MinUsableFrames` usable frames.
- All 36 targets of a run must be there (`GridWindow.AllTargetIndexes`).

Validation returns target indexes and the missing feature names so the failure
message can identify the affected targets.

### Recovery policy

[`CalibrationRecoveryPolicy.cs`](../../app/Calibration/GazeGrid/CalibrationRecoveryPolicy.cs)
defines the three-stage recovery policy:

1. The first validation failure starts a complete calibration again.
2. The second validation failure retries only the affected target indexes.
3. A failure after the target retry goes straight to the failure screen and does
   not proceed to saving or preview.

Only these automatic restarts count as attempts. Choosing to start again (the
intro, "Do the grid again", or the failure screen) starts the steps over.

A retried head-step dot asks for the head position again, on screen and silently,
before it fills. Without that it would be recorded sitting normally and teach the
fit the wrong thing.

### Feedback

The check runs as soon as a run ends, before the result screen
(`CalibrationCompleted` in `App.StartGazeCalibration`), so the screen can't say
everything was captured and then fail. If it fails, the screen says what went wrong
and where, from
[`CalibrationFeedback.cs`](../../app/Calibration/GazeGrid/CalibrationFeedback.cs),
for example "Your eyes looked closed. Keep both open while each ring fills, and
blink between dots. (2 dots: bottom left, bottom middle)". It works out the cause
from the failed dots' frames: too few frames from the camera, the face out of view,
eyes not found, eyes covered or eyes closed.

Nothing here is spoken. Everything can be answered by looking: on a failed check,
right takes the next try and left closes. On the failure screen, right starts again
and left closes.

A head position that wasn't reached adds a note to the result screen but doesn't
fail the check. Someone who can't move their head that far still has to be able to
finish, and the fit already leaves out what didn't move enough.

[`CalibrationSession.cs`](../../app/Calibration/GazeGrid/CalibrationSession.cs)
holds the attempt state, target summaries, validation result, and callback
actions for this policy.

[`App.xaml.cs`](../../app/App.xaml.cs) wires the session into the actual
calibration flow. Captured targets are stored in a dictionary keyed by target
index, so a target retry replaces the old target data instead of appending a
duplicate summary.

### Calibration window behavior

[`GridWindow.xaml.cs`](../../app/Calibration/GazeGrid/GridWindow.xaml.cs)
supports asynchronous acceptance through `Func<Task<bool>>`. This allows the
target-only retry to finish before fitting or saving continues.

[`GridWindow.Results.cs`](../../app/Calibration/GazeGrid/GridWindow.Results.cs)
provides `ShowCalibrationFailure`, which displays the validation details and
changes the window to `Phase.Failure`.

## Automated verification completed

The following tests are present:

- [`GazeTargetSummaryTests.cs`](../../tests/app.Tests/Calibration/GazeGrid/GazeTargetSummaryTests.cs)
  covers aggregation, required features, and the minimum usable-frame rule.
- [`CalibrationRecoveryTests.cs`](../../tests/app.Tests/Calibration/GazeGrid/CalibrationRecoveryTests.cs)
  covers the recovery-policy decisions.
- [`CalibrationSessionTests.cs`](../../tests/app.Tests/Calibration/GazeGrid/CalibrationSessionTests.cs)
  covers the first full restart, second target-only retry, and third-failure
  behavior using deterministic invalid target summaries.

The last verified run completed with:

```text
241 tests passed
0 tests failed
Application build succeeded
0 build errors
0 build warnings
```

## Verification limits and follow-up

The automated session tests do not start a real WPF window or webcam tracker.
They verify the recovery coordinator through callbacks. A manual run is still
needed to verify tracker timing, real closed-eye frames, target presentation,
the feedback text on screen, and answering the failure screen by looking.

The worktree also had trailing-whitespace reports in
[`App.xaml.cs`](../../app/App.xaml.cs) and
[`GridWindow.xaml.cs`](../../app/Calibration/GazeGrid/GridWindow.xaml.cs) at
the last verification. These do not affect the build or tests but should be
cleaned before finalizing the change.

## Manual recovery checklist

Run the application with:

```powershell
Set-Location .\app
dotnet run -- --calibrate
```

Verify that:

1. A target with insufficient usable frames causes a complete recalibration
   on the first failure.
2. A second failure retries only the affected target indexes.
3. A third failure shows the failure message, does not save a calibration, and
   can be answered by looking: right starts again, left closes.
4. A successful validation proceeds to fitting, saving, and preview.
