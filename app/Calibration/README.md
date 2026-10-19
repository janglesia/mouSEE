# Calibration/

Everything that makes and keeps a calibration: what mouSEE learns about one person, saved as one file. See [docs/calibration/](../../docs/calibration/).

A new calibration is the **eye check**, then the **grid** (with the head step at the end). `App.StartCalibration()` runs them in that order.

| | |
|---|---|
| [EyeCheck/](EyeCheck/) | part 1: open and closed eyes, and how you hold your head |
| [GazeGrid/](GazeGrid/) | part 2: the 20-point grid and the head step, and the math that turns them into gaze numbers |
| [Saving/](Saving/) | the calibration file, the saved ones, the raw frames, and the calibrations window |
| [Accuracy/](Accuracy/) | the accuracy test: how far off the bubble is, nothing changes |
| `Saved/` | your calibrations, made by the app, not in git |
