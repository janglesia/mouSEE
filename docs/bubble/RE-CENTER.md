# Re-centering

The way you sit changes a bit from one day (or hour) to the next, and the calibration only knows how you sat then. On the second accuracy test the head sat 3° higher than during calibration, and everything landed about 4% too far left. A re-center fixes most of that: look at a dot in the middle of the screen for a moment, and however far off the bubble was is taken off every bubble position after. On that test it took left/right from 6.6% to 4.5% and up/down from 11.7% to 8.8%.

- it runs when mouSEE starts with a calibration (setting `calibration.recenterOnStart`, see [SETTINGS.md](../SETTINGS.md)), with **Ctrl+Alt+R** from any program, or **r** in the camera window
- a dot with a ring, 1.5 s for the eyes to get there, then 1.5 s recorded. It closes by itself. Esc skips it
- nothing is said out loud: it runs every time mouSEE starts, and a voice then gets old fast. A problem (no camera, eyes not seen) is shown for a few seconds instead
- it needs at least 15 frames with a bubble. If the bubble was more than 30% of the screen off, it doesn't re-center: that's not drift anymore, a new calibration works better
- the shift is kept until mouSEE closes or another calibration is used. It's applied to the bubble and the accuracy test (whose report says which shift was in use: `offsetX`, `offsetY`)

In code: `Recenter.From()` (app/Bubble/Recenter.cs) works it out from the frames, `RecenterWindow` shows the dot, `App.Offset` / `App.SetOffset()` hold it.
