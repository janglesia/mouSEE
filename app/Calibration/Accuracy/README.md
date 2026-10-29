# Accuracy/

The accuracy test and fine-tune: dots all over the screen, sitting normally, leaning and tilting, with the bubble hidden. It measures how far off the bubble lands, then fine-tunes the calibration from the same dots (app/Bubble/FineTune.cs), kept if you want it. Results go in `app/Calibration/Saved/tests/`. See [docs/calibration/ACCURACY-TEST.md](../../../docs/calibration/ACCURACY-TEST.md).

| | |
|---|---|
| `AccuracyTest.cs` | the dots, the phases, and the report |
| `AccuracyTestWindow.xaml(.cs)` | runs it, draws where the bubble landed, works out the fine-tune and shows it live to keep or not. Ctrl+Alt+F |

Tests: `tests/app.Tests/Calibration/Accuracy/`.
