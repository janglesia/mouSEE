# Calibration/

Everything that makes and keeps a calibration: what mouSEE learns about one person, saved as one file. See [docs/calibration/](../../docs/calibration/).

A new calibration is the **grid** (with the head step at the end), then the **eye check**, which takes the grid's open eyes and head angle. `App.StartCalibration()` runs them in that order.

| | |
|---|---|
| [GazeGrid/](GazeGrid/) | part 1: the 20-point grid and the head step, and the math that turns them into gaze numbers |
| [EyeCheck/](EyeCheck/) | part 2: when your eyes count as closed |
| [Saving/](Saving/) | the calibration file, the saved ones, the raw frames, and the calibrations window |
| [Accuracy/](Accuracy/) | the accuracy test: how far off the bubble is, nothing changes |
| `Saved/` | your calibrations, made by the app, not in git |
