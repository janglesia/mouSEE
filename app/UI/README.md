# UI/

The overlay, and pieces the other windows share. A window that belongs to one feature lives in that feature's folder (the grid screen in `Calibration/GazeGrid/`...), not here.

| | |
|---|---|
| `OverlayWindow.xaml(.cs)` | always on top, covering the screen without getting in the way: the bubble, the status bar, the debug readout, the where-to-sit bar, and the global Ctrl+Alt keys |
| `MiddleDot.xaml(.cs)` | the dot shown whenever a screen asks you to look at the middle |
| `Speaker.cs` | reads instructions out loud, and the sound at the end of a step |
| `SettingSlider.cs` | a slider for one setting, built from its description |
| `UiColors.cs` | the calibration yellow and dark, and which one the middle dot is |

Tests: `tests/app.Tests/UI/`.
