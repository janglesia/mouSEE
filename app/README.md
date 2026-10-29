# app/

The C# Windows app (WPF). It starts the Python tracker, reads what it sends, and does everything you see: the bubble, the status bar, the calibration screens. The big picture is in [docs/OVERVIEW.md](../docs/OVERVIEW.md).

| | |
|---|---|
| `App.xaml(.cs)` | starts the app: the overlay, the Ctrl+Alt keys and camera-window keys, which calibration screen opens, running a calibration (grid, saving and fitting, then the eye check), the fine-tune's fitting again (`FitWithFineTune`), and `Bubble()`, where the bubble goes |
| `AppPaths.cs` | where settings and calibrations are saved |
| `Stats.cs` | percentiles, and `Spread` (how a number was spread over a recording), used everywhere |
| `mouSEE.csproj` | the project. Its `<Using>` list is every folder below, so any file can use any part without a `using` line |
| [Tracker/](Tracker/) | talking to the Python tracker |
| [Calibration/](Calibration/) | making and keeping a calibration |
| [Bubble/](Bubble/) | what happens to the bubble after the calibration |
| [LookToAnswer/](LookToAnswer/) | yes / no without hands |
| [UI/](UI/) | the overlay and pieces the windows share |
| [Settings/](Settings/) | every user setting |
| [Input/](Input/) | empty: moving the cursor and clicking go here |
| `assets/` | the logo |

A folder holds one feature: its logic and its window together. The namespace is the folder (`app.Calibration.GazeGrid`...). The tests are in `tests/app.Tests/`, in the same folders.
