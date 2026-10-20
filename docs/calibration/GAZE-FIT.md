# How the gaze is fitted

When you choose **Try it out** on the grid screen's result, `App.StartGazeCalibration()` fits the left/right and up/down numbers to the targets and what moving your head does to the head step (`GazeFit.Fit`, app/Calibration/GazeGrid/GazeFit.cs) and saves them in `gaze`, with the report in `gazeFit`. The tracker restarts and the bubble uses them.

### The preview

Then comes the preview, like the fine-tune's: the 20 points stay up as rings, and a red dot shows live where the bubble lands with the new calibration (the tracker's point, no fine-tune or re-center yet). Look at the rings, then close your eyes for 2 seconds and look right to **keep it** or left to **do the grid again** (the buttons work too, and Esc keeps it). Doing it again puts back what was saved: the calibration as it was before the grid, or none if the grid made a new one (`GridWindow.Results.cs`, `CalibrationDiscarded` in `App.StartGazeCalibration`). This replaces running the fine-tune after the grid: the fine-tune's correction can't come from the 20 points themselves, the fit was made on them, so the bubble already lands as close to them as it can.

It's the model in `estimate_gaze()` (tracker/gaze_model.py), so what the fit measures is what the bubble does: each eye's angle is `asin((h - baseline) / k_h)`, the two are averaged, and `x = center_x + gain_x * tan(angle)`.

- **baselines**: where each iris sits when you look at the middle column. The eyes turn in a bit, so they're different for each eye (left -0.14, right +0.13 in the first recording)
- **k_h**: tried from 0.5 to 3, keeping the one that's best on targets it wasn't fitted on. Bigger is more linear. Real eyes have fit better with a big one than with the default 0.7
- **center_x, gain_x**: a straight line through `tan(angle)` against where the targets were

Only the targets with at least 10 frames where both eyes were open count. Besides the grid's that's the [fine-tune's dots](#the-fine-tunes-dots), not the head step's (or the accuracy check targets some old recordings have). It needs at least 3 targets in at least 2 columns, otherwise the gaze stays at the defaults.

The first real calibration: the default numbers put the left and right targets at -0.7 and 1.7 (way off the screen, the bubble was always further out than where you looked). Fitted, the targets land on average 3.5% of the screen width away (about 90 px on 2560), 10% at worst (bottom left, looking down changes how the eyes read), and about 4.6% on targets left out of the fit.

## Looking down

Looking down, the iris moves less from side to side (the lid comes down over it), so left/right gets squeezed together at the bottom of the screen. The lid openness says how far down you're looking, so the left/right fit has a third number next to center and gain:

```
x = center_x + gain_x * tan(angle) + x_squeeze * tan(angle) * (lid openness - openness_ref)
```

`openness_ref` is the median openness over the grid targets. It's only fitted when every target has the openness and it changes by at least 0.03 over them (it does between the rows). On the first accuracy test it took left/right from 4.4% to 3.8% sitting normally and leaning left from 6.6% to 5.3%.

## Moving your head: the head step

The iris position only says where the eyes point in the head. Lean to the right and look at the same spot, and your eyes turn a little left to stay on it, so without a correction the bubble moves left. The accuracy tests showed leaning does more than shift it: it also shrinks left/right by about a quarter (the eyes move less for the same distance on the screen), and it moves up/down too. Tilting the head moves up/down as well.

So after the grid targets comes the head step, 16 dots (`HeadPose.Step` in app/Calibration/GazeGrid/CalibrationTarget.cs). Each new position is said out loud and shown in big letters in the middle first, then guided live until the head is there, with how far it is now: "Tilt your head a little more up (3° of 6°)", "Lean a little more to the left (4% of 7%)", "A little less, not so far (14°, at most 12°)", and in green "Good, hold it there (9°)" (`HeadPose.Guide`). Tilting is in degrees, leaning in how far the middle of your face moved across the camera image. The guidance starts as the position is being read out, not after, and what's said out loud leaves the numbers out. The dots start once it's been in range for 0.8 s, and the hint stays up while they run. If it doesn't get there within 12 s it goes on anyway, and the fit leaves out what didn't move enough.

How far is measured, not "a little": leaning means the middle of the face moves 7-16% of the camera image, tilting 6-12° of head pitch, both from how the head sat during the grid targets. That doesn't depend on the screen size, and it's about how much people move while using the computer. The first head step said "tilt your head up a little" and got 14° up and 20° down: on those dots the bubble was still up to 38% off, because the fit tried to match extremes nobody uses day to day.

| How | Dots |
|-----|------|
| lean to the left and stay there | near the 4 corners: left and right edge (12% in), at 20% and 80% of the height |
| lean to the right and stay there | the same 4, the other way round |
| sit normally and tilt your head up a little | near the 4 corners: top and bottom edge (12% in), at 20% and 80% of the width |
| tilt your head down a little | the same 4, the other way round |

It was 2 dots per position: leaning only on the middle row and tilting only in the middle column. Then the fit never saw what leaning does at the top or bottom of the screen, or what tilting does on the left or right, and 8 dots had to pin down 4 numbers. Moving even a little away from how you sat during the grid then threw the bubble off.

For each dot the rest of the fit says where the bubble would land, and how far that is from the dot is what the head did. `GazeFit.FitHead` fits:

```
x += head_x_gain * moved + head_x_shrink * tan(angle) * |moved|
y += head_y_pitch * (pitch - pitch_ref) + head_y_lean * |moved|
```

`moved` is how far the middle of the face moved in the camera image from where it was during the grid targets (`head_x_ref`), `pitch` the head pitch from the tracker (`pitch_ref` the one during the grid targets). The tracker passes both to `estimate_gaze()` every frame. Each correction is only fitted when the head moved enough for it (the face 3% of the camera image while leaning, the pitch 4° between the tilt dots), and left out if it comes out too big to be right (probably looked away from a dot). `gazeFit.headExplanation` says what was fitted, and how far off the head step's dots were without and with the corrections.

The y corrections need the fitted up/down. Calibrations from before the head step had one dot in the middle to lean at; those still get the old left/right shift from it (`GazeFit.FitHeadX`).

## Up and down

Two things move when you look up or down: the iris, and the lids (more open looking up, less looking down: 0.34 at the top row, 0.31 in the middle, 0.27 at the bottom in the first calibration with this). Up/down is fitted from both (`GazeFit.FitY`):

```
y = y_const + y_openness * lid openness + y_iris * vertical iris offset
```

both averaged over the open eyes, a straight line through the grid targets. It needs at least 4 targets in at least 2 rows with lid openness measured, otherwise up/down stays at the defaults (`gazeFit.yExplanation` says why).

The vertical iris offset is where the iris is above or below the line between the eye corners (`eye_extractor.py`), the same way left/right is measured from the corners. The corners stay put on the head, so the iris moving past them is the eye turning.

It used to be measured from the middle between the lids. But the lids follow the eye, so that cancelled most of it out (0.035 from the top row to the bottom row, against 0.8 left to right). Up/down then came almost entirely from the lid openness: about 8 pixels of lid gap for the whole screen height, so a slight head tilt or a squint moved the bubble a third of the screen.

Calibrations recorded before the change (recording `version` 1) have the old offset, which can't be mixed with the new one. When they're fitted again they leave it out and fit up/down from the lids only (`y_iris` 0, `GazeTargetSummary.WithoutVertical`). They keep working as before, but making a new calibration gets the better up/down.

## The curves

A straight line can't bend, and every fine-tune found the same two bends (see [Why the curves](../bubble/FINE-TUNE.md#why-the-curves-where-the-camera-is)): the sides of the screen reading higher than the middle, and up/down stretched more at one end. So up/down can have two curves on top:

```
+ y_arc * tan(left/right angle)²      the sides higher or lower than the middle (an arc)
+ y_openness_sq * lid openness²       stretched more at one end than the other
```

| shape (`gazeFit.yShape`) | numbers |
|---|---|
| `flat` | the straight line |
| `arc` | + `y_arc` |
| `arc-and-uneven` | + `y_arc` and `y_openness_sq` |

Each shape is fitted without one target and checked on it, for every target, and the one that does best is kept. A curve is only picked when it's at least 5% better than the simpler shape, and when there are at least 3 more targets than numbers. The tan is the same left/right angle the x fit uses, so the arc needs that fitted first. On the earlier 9-point calibrations the curves took up/down on left-out targets from 13.7% to 9.2%, 7.5% to 5.0%, 12.2% to 10.3% and 7.5% to 7.0%. With 3 rows there was barely anything to see a curve with, which is why the grid has 4 rows and 5 columns now.

## The grid

20 targets, 5 columns by 4 rows, 7% from the edges, in snake order (`GridWindow.BuildTargets`). It was 3 by 3 at 8% from the edges. Going further into the corners wouldn't have helped: on the 03:07 calibration left/right was within 1% at 7 of the 9 targets, and up/down was 3-12% off at the targets themselves, so the problem was the straight line, not how far out the targets were. More points in between let the fit see the curves. Very close to the edges would also cut off the logo, and at the bottom the lids cover more of the iris. Head pitch would fit sitting still slightly better but breaks when leaning (leaning changes it), so it's not used.

On the first accuracy test this took the up/down error from 24% of the screen height (the defaults) to 11% sitting normally and 4% leaning left. Leaning right got worse (6% to 14%, the eyes open unevenly then), which the head step is about (see [Moving your head](#moving-your-head-the-head-step)). Staring at one point the bubble shakes about 2% of the height up and down, more than left/right (0.8%), since the lids are a small number made big.

## The fine-tune's dots

The [fine-tune](../bubble/FINE-TUNE.md) (run with the accuracy test) records 20 dots the same way as the grid (eyes on known points, sitting normally) and 12 while leaning and tilting like the head step, on a later day. They're added to the calibration's targets and the whole fit runs again: the sitting normally ones count like the grid's, the others like the head step's (`App.FitWithFineTune`), so the calibration gets 40 points with however you sat on both days, instead of the dots only feeding a correction on top. A newer fine-tune replaces the older one's dots. They're saved like the grid's: `fineTune: true` on the targets (`gazeTargets`, numbered from 100) and on their frames in the recording, so fitting again later keeps them.

## Fitting again

Each fit says which version of the fit made it (`gazeFit.version`, `GazeFit.Version`). When mouSEE starts, every calibration fitted by an older version gets fitted again from its recording (`App.FitOldCalibrations`), so improvements to the fit apply to calibrations that were already made. Bump the version when the fit changes. A calibration without a recording gets fitted again from the target summaries in its file, with up/down from the lids only (the summaries have the old vertical offset, see [Up and down](#up-and-down)).

Calibrations made before the fit existed have the targets but empty gaze numbers, those get fitted the same way.

## What's left

- **leaning closer or further**: the face gets bigger or smaller in the image, which changes how far the eyes turn for the same spot. The face width is recorded (`faceW`), it could be fitted with a lean forward and back part in the head step. For now the overlay says when you're too close or too far (see [Where to sit](../bubble/WHERE-TO-SIT.md))
- **turning the head**: head yaw is recorded but not used. While leaning people also turn their head a bit, which the leaning correction partly takes in
- **jitter**: staring at one point, the bubble still moves from frame to frame, about 1% of the screen width left/right and 2% of the height up/down. That's the smoothing task (filter the bubble's position)
