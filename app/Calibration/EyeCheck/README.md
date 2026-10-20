# EyeCheck/

Part 2 of a calibration, after the grid: when your eyes count as closed. You close both eyes, then the same with your head tilted up and down, and it's compared with the open eyes the grid recorded. Spoken instructions, under a minute, no hands. See [docs/calibration/EYE-CALIBRATION.md](../../../docs/calibration/EYE-CALIBRATION.md).

| | |
|---|---|
| `EyeCheckWindow.xaml(.cs)` | runs it: says each step, records the frames, repeats a step that didn't work, shows the result |
| `EyeCheckSteps.cs` | the steps (what to say, how long), how each one is checked, the live hints, the resting head angle from the grid |
| `EyeCheckAnalysis.cs` | turns the grid's open eyes and the closed steps into the eye values the tracker uses (`EyeThresholds`) |
| `EyeCheckRound.cs` | the last step: closing both eyes and opening them, once each, checking the new value works |
| `EyeCheckReport.cs` | what's saved: why each value is what it is, and the raw frames of each step |

Tests: `tests/app.Tests/Calibration/EyeCheck/`.
