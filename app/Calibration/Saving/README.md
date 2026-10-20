# Saving/

Keeping calibrations: one file each in `app/Calibration/Saved/`, and the last one you used is the one in use. See [docs/calibration/SAVED-CALIBRATIONS.md](../../../docs/calibration/SAVED-CALIBRATIONS.md).

| | |
|---|---|
| `CalibrationProfile.cs` | one saved calibration: its gaze numbers (`GazeModel`), eye values (`EyeThresholds`), the fine-tune, and the reports |
| `CalibrationStore.cs` | all of them: list, add, use, rename, delete, and their recordings. One for the whole app (`App.Calibrations`) |
| `CalibrationRecording.cs` | the raw frames of a calibration, in their own file (`Saved/recordings/`) |
| `CalibrationListWindow.xaml(.cs)` | the calibrations window (Ctrl+Alt+C): pick, delete, make a new one, test and fine-tune, the head mode and the hint sliders. Also the "calibrate now?" question on first start |

Tests: `tests/app.Tests/Calibration/Saving/`.
