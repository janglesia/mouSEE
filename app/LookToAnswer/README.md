# LookToAnswer/

Yes / no without hands: look right for yes, left for no, and hold it for 3 seconds. Every question in the calibration screens uses it. See [docs/LOOK-TO-ANSWER.md](../../docs/LOOK-TO-ANSWER.md).

| | |
|---|---|
| `LookChoice.cs` | the logic: which side, for how long, back to the middle before the next answer |
| `ClosedEyesGate.cs` | "done looking": on screens you look around on, the eyes closed for 1.5 s before left / right counts |
| `ChoicePrompt.xaml(.cs)` | the No / Yes bars, put in any window: `Choice.Ask("Begin", "Cancel")`, then handle `Choice.Chosen` |

Tests: `tests/app.Tests/LookToAnswer/`.
