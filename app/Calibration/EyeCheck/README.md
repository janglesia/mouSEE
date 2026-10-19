# EyeCheck/

Part 1 of a calibration: what your eyes look like open and closed, and how you hold your head. Spoken instructions, about a minute and a half, no hands. See [docs/calibration/EYE-CALIBRATION.md](../../../docs/calibration/EYE-CALIBRATION.md).

| | |
|---|---|
| `EyeCheckWindow.xaml(.cs)` | runs it: says each step, records the frames, repeats a step that didn't work, shows the result |
| `EyeCheckSteps.cs` | the steps (what to say, how long), how each one is checked, the live hints |
| `EyeCheckAnalysis.cs` | turns the recordings into the eye values the tracker uses (`EyeThresholds`) |
| `EyeCheckRound.cs` | the last step: a random mix of "close one eye", checking the new values work |
| `EyeCheckReport.cs` | what's saved: why each value is what it is, and the raw frames of each step |

Tests: `tests/app.Tests/Calibration/EyeCheck/`.
