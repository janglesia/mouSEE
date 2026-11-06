# Smoothing the bubble

Staring at one spot, the bubble shakes: about 1% of the screen width left/right and 2% of the height up/down, frame to frame. That's the landmarks jittering by a fraction of a pixel, made big. Smoothing takes most of it out without making the bubble slow to follow your eyes.

Turn it on or off in the calibrations window (Ctrl+Alt+C), under **Smoothing**, with the **Sensitivity** slider under it. Or in settings.json: `cursor.smoothing` and `cursor.sensitivity` (see [SETTINGS.md](../SETTINGS.md)). On by default. It applies right away.

## How

A [1 euro filter](https://gery.casiez.net/1euro/) on each direction (`app/Bubble/OneEuroFilter.cs`): a low-pass filter whose cutoff goes up with how fast the bubble moves. Resting on a spot it filters hard, so the shake goes away. When your eyes jump somewhere else it hardly filters, so the bubble isn't dragged out behind them. That's what a plain average can't do: steady enough to take the shake out, it lags behind every jump.

- the cutoff at rest comes from **Sensitivity**: 0.05 Hz (0, steadiest) to 3 Hz (1, quickest), about 0.4 Hz in the middle
- speeds up to 1 share of the screen per second don't count as moving. The shake of a resting gaze reads as about that much, and on its own it kept loosening the filter, which made the bubble bounce. This is an addition to the standard filter
- above that the cutoff rises by 5 Hz per share of the screen per second (beta)
- both directions share one speed, the bubble's overall speed. With one each, a move that was mostly sideways went across first and only crept up or down afterwards, an L instead of a straight line, because the small part of it looked like shake
- after a gap of more than 0.3 s (eyes closed, face lost, paused) it starts over, so the bubble appears where you look rather than sliding in from where it was

Where it goes in the bubble's steps: the tracker's position, the fine-tune, the re-center's shift, then the smoothing, then the head nudge (smoothed first, so it notices sooner that your eyes have come to rest), then kept on the screen. The calibration screens' previews have their own lighter smoothing, the accuracy test and the fine-tune use the bubble's positions without it.

## What it does

Measured by replaying the shake from one real recording (one person, about 60 fps) through the filter, with moves added on top. "Bounce" is how far the bubble travels up/down per second while the eyes rest on a spot, in % of the screen height. Before filtering it's about 20. The moves are up or down, and the time is to get 90% of the way there and stay.

| Sensitivity | Bounce | Before ignoring shake | Move of 1% | 3% | 5% | 10% | 50% |
|---|---|---|---|---|---|---|---|
| 0 (steadiest) | 1.3 | 4.0 | 6.4 s | 0.6 s | 0.17 s | 0.12 s | 0.03 s |
| 0.25 | 1.7 | | 2.6 s | 0.5 s | 0.17 s | 0.12 s | 0.03 s |
| 0.5 (default) | 2.6 | 5.8 | 0.7 s | 0.4 s | 0.17 s | 0.12 s | 0.03 s |
| 0.75 | 4.9 | | 0.16 s | 0.24 s | 0.17 s | 0.12 s | 0.03 s |
| 1 (quickest) | 9.0 | 9.4 | 0.11 s | 0.12 s | 0.12 s | 0.12 s | 0.03 s |

So the slider decides how still the bubble holds against how quickly the smallest moves are followed. Moves of 5% and up are quick at any setting. At the low end, moves of 1% are slow, because they look just like the shake. The "before" column is the earlier version, with its own range of 0.3 to 3 Hz and beta 1.5.

Left/right moves of 5-10% measure slower, 0.3-0.6 s. Even at sensitivity 1, which hardly filters, they take about 0.3 s, so some of that is the left/right reading wandering slowly in the recording rather than the filter. 50% moves take about 0.04 s.

The threshold is a speed, and the filter reads less speed from the same shake at a lower frame rate, so it may need another look on a slower camera.

Tests: `tests/app.Tests/Bubble/OneEuroFilterTests.cs`.
