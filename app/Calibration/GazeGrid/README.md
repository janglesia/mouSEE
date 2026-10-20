# GazeGrid/

Part 1 of a calibration: where on the screen your eyes point. The mouSEE logo goes to 20 places (5 by 4), then the head step (leaning and tilting), and the gaze numbers are fitted to it. See [docs/calibration/GAZE-FIT.md](../../../docs/calibration/GAZE-FIT.md) and [docs/calibration/GRID-POINTS.md](../../../docs/calibration/GRID-POINTS.md).

| | |
|---|---|
| `GridWindow.xaml(.cs)` | the grid screen: setup, the tracker, which panel shows |
| `GridWindow.Sequence.cs` | the run: the 20 points, then the head step, guided |
| `GridWindow.Results.cs` | the result screen, and the preview: the 20 points as rings and the bubble live with the new calibration, keep it or do the grid again |
| `CalibrationTarget.cs` | one point, and the head step's poses (`HeadPose`: which way, how far) |
| `GazeTargetSummary.cs` | what the eyes did at each point: what the fit uses |
| `GazeFit.cs` | the fit: runs the three below, the shared math, and `Predict` (where the tracker puts the bubble with some gaze numbers, for the fine-tune) |
| `GazeFit.LeftRight.cs` | left/right (`FitX`) |
| `GazeFit.UpDown.cs` | up/down, with the curves when they help (`FitY`) |
| `GazeFit.Head.cs` | leaning and tilting, from the head step (`FitHead`) |
| `GazeFitReport.cs` | what the fit found, saved with the calibration |

The numbers end up in `tracker/gaze_model.py`, which uses them every frame. Tests: `tests/app.Tests/Calibration/GazeGrid/`.
