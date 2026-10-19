# Bubble/

What happens to the bubble after the calibration has put it roughly where you look: small corrections for how you sit today, what moving your head does, and telling you when you moved too far. See [docs/bubble/](../../docs/bubble/).

Where the bubble goes, every frame: the tracker's point, then the fine-tune, then the re-center's shift (`App.Bubble()`), then the head nudge if it's on (`OverlayWindow`).

| | |
|---|---|
| `Recenter.cs`, `RecenterWindow.xaml(.cs)` | re-center: look at a dot in the middle for a moment, the shift is taken off every bubble position. Every start, or Ctrl+Alt+R |
| `FineTune.cs`, `FineTuneWindow.xaml(.cs)` | fine-tune: 20 dots, then a last correction on the bubble, kept with the calibration. Ctrl+Alt+F |
| `PositionGuide.cs` | the where-to-sit hints on the overlay ("Move a little to your left", "Tilt your head up a little") |
| `HeadPosition.cs` | where the head is: face position, size and tilt |
| [HeadModes/](HeadModes/) | what moving your head does: nothing (Eyes only), or pushing the bubble once it holds (Eyes + head nudge) |

Tests: `tests/app.Tests/Bubble/`.
