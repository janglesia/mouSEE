# Accuracy/

The accuracy test: dots all over the screen, sitting normally, leaning and tilting, with the bubble hidden. It only measures how far off the bubble lands, nothing changes. Results go in `app/Calibration/Saved/tests/`. See [docs/calibration/ACCURACY-TEST.md](../../../docs/calibration/ACCURACY-TEST.md).

| | |
|---|---|
| `AccuracyTest.cs` | the dots, the phases, and the report |
| `AccuracyTestWindow.xaml(.cs)` | runs it and draws where the bubble landed |

Tests: `tests/app.Tests/Calibration/Accuracy/`.
