# Smoothing the bubble

Staring at one spot, the bubble shakes: about 1% of the screen width left/right and 2% of the height up/down, frame to frame. That's the landmarks jittering by a fraction of a pixel, made big. Smoothing takes most of it out without making the bubble slow to follow your eyes.

Turn it on or off in the calibrations window (Ctrl+Alt+C), under **Smoothing**, with the **Sensitivity** slider under it. Or in settings.json: `cursor.smoothing` and `cursor.sensitivity` (see [SETTINGS.md](../SETTINGS.md)). On by default. It applies right away.

## How

A [1 euro filter](https://gery.casiez.net/1euro/) on each direction (`app/Bubble/OneEuroFilter.cs`): a low-pass filter whose cutoff goes up with how fast the bubble moves. Resting on a spot it filters hard, so the shake goes away. When your eyes jump somewhere else it hardly filters, so the bubble isn't dragged out behind them. That's what a plain average can't do: steady enough to take the shake out, it lags behind every jump.

- the cutoff at rest comes from **Sensitivity**: 0.3 Hz (0, steadiest) to 3 Hz (1, quickest), about 1 Hz in the middle
- it rises by 1.5 Hz per share of the screen per second (beta). A jump across half the screen is about 10 per second, so the cutoff goes up to ~15 Hz
- after a gap of more than 0.3 s (eyes closed, face lost, paused) it starts over, so the bubble appears where you look rather than sliding in from where it was

Where it goes in the bubble's steps: the tracker's position, the fine-tune, the re-center's shift, then the smoothing, then the head nudge (smoothed first, so it notices sooner that your eyes have come to rest), then kept on the screen. The calibration screens' previews have their own lighter smoothing, the accuracy test and the fine-tune use the bubble's positions without it.

## What it does (simulated, 60 fps, the shake above)

| Sensitivity | Shake left at rest | A jump across the screen, 90% there | A small move (5% of the screen), 90% there |
|---|---|---|---|
| 0 (steadiest) | 19% | 50 ms | 230 ms |
| 0.5 (default) | 26% | 50 ms | 180 ms |
| 1 (quickest) | 39% | 50 ms | 100 ms |

So the slider mostly decides how quickly small moves are followed, against how still it holds. Big jumps are quick at any setting. Beta was picked from 0.3, 0.6, 1 and 1.5: lower takes out only a little more shake (13-17% left at the steadiest) but makes jumps 2-3 times slower.

Tests: `tests/app.Tests/Bubble/OneEuroFilterTests.cs`.
