# Fine-tuning

The calibration learns from the eyes: where the irises and lids are for each dot. The fine-tune learns from where the bubble ends up, so it fixes whatever the calibration still gets wrong. 20 dots show up around the screen (5 columns, 4 rows, close to the corners), the bubble is hidden so you can't follow it, and for each dot it compares where the bubble landed with where the dot was. From that it works out a correction on the bubble, per direction:

```
x' = x0 + x1 * x + x2 * y + xuv * u * v + xuu * u² + xvv * v²
y' = y0 + y1 * x + y2 * y + yuv * u * v + yuu * u² + yvv * v²
u = x - 0.5, v = y - 0.5
```

`x`, `y` is where the tracker put the bubble, `x'`, `y'` where it goes. The first three numbers are a shift (`x0`, `y0`), a stretch (`x1`, `y2`), and how much one direction leaks into the other (`x2`, `y1`). The rest are curves around the middle of the screen, the ones a flat correction can't fix:

| shape | for | what it fixes |
|---|---|---|
| `flat` | both | shifted and stretched, nothing else |
| `wider-at-one-end` | left/right | left/right stretched at the top and squeezed at the bottom (or the other way): overshooting into the top corners, never reaching the bottom ones (`xuv`) |
| `flat-and-wider-at-one-end` | left/right | both of the above |
| `arc` | up/down | the sides of the screen landing higher or lower than the middle, an arc (`yuu`) |
| `arc-and-uneven` | up/down | the arc, and up/down stretched more at one end than the other (`yvv`) |

Each direction tries its shapes and keeps the one that does best on dots it wasn't fitted on. A shape with more numbers is only picked when it's at least 5% better than the simpler one, since more numbers can always follow a few dots more closely.

## Why the curves: where the camera is

The first real fine-tune was flat only, and it left exactly those curves: with the camera under the screen tilted up, the sides landed higher than the middle (on the top row the edge dots were 31-41% of the height too high, the middle ones 16-20%), the top right and left overshot into the corners, and the bottom corners were never reached. The camera sees the eyes from below, so when they turn sideways the lids look closer together or further apart than straight on, which the up/down fit reads as looking higher or lower. And how far the iris moves sideways in the image depends on how far up you look. On those 12 dots the curves took left/right from 4.6% to 3.9% (the flat one couldn't improve it at all) and up/down from 5.8% to 3.2%. Where the camera sits doesn't need to be set anywhere: the fine-tune measures what it does, wherever it is. It does mean a fine-tune is only right for that camera position, move the camera and fine-tune again.

- it opens with **Fine-tune** in the calibrations window, **Ctrl+Alt+F** from any program, **f** in the camera window, or `make run ARGS="--fine-tune"`. It needs a calibration to keep it in. It used to open by itself after the grid screen, but with the 20-point grid fitting the curves itself it isn't needed after every calibration, it's for when the bubble is off on a later day
- each dot: 0.8 s for the eyes to get there, then 1.5 s recorded, the median of the bubble's position. A dot needs at least 15 frames with a bubble, and it needs at least 8 dots
- each direction is checked on its own: fitted without one dot and measured on that dot, for every dot. A direction is only corrected when its best shape comes out at least 10% better than no correction. A shape that would stretch more than 2.5 times, or curve a corner by more than half the screen, isn't used (that's not a last adjustment, the calibration doesn't fit). The other direction stays as it is
- then you try it out: the dots stay up as rings and two bubbles follow your eyes, yellow with the fine-tune and grey how it is now (with the old fine-tune and re-center, which is why the grey one can sit off to one side). When you're done looking, close your eyes for 1.5 s, then look right for 3 s to keep it, left to discard it (or the buttons). See [Answering by looking](../LOOK-TO-ANSWER.md)
- keeping it saves it with the calibration (`fineTune`, `fineTuneReport`) and resets the re-center, because the fine-tune already covers how you sit right now
- it's made on top of the gaze numbers, so when they're fitted again (a new grid screen, or [fitting again](../calibration/GAZE-FIT.md#fitting-again)) it's dropped

Where the bubble goes, in order: the tracker's position, the fine-tune, then the re-center's shift. That's the bubble, the accuracy test, and the re-center itself (which measures what's left after the fine-tune).

Tried on the first two accuracy tests (fitted on the sitting-normally dots, measured on dots left out): up/down went from 24% to 11% and from 11% to 5.4% of the screen height. Left/right went from 6.5% to 5.0% on one and slightly worse (4.4% to 4.9%) on the other, which is why each direction is only corrected when it helps.

In code: `FineTune.Fit()` (app/Bubble/FineTune.cs) works it out, `FineTuneWindow` shows the dots and the preview, `App.Bubble()` applies it, `App.SaveFineTune()` keeps it.
