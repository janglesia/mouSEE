# Head modes

What moving your head does to the bubble. There are three modes to try against each other, picked in the calibrations window (Ctrl+Alt+C, or c in the camera window) under **What your head does**. It's saved as the setting `head.mode` (see [SETTINGS.md](../SETTINGS.md#head)) and applies right away.

There are two separate switches, and each mode is a combination of them:

- **Head correction:** the calibration undoes what leaning and tilting do to the eye reading, so moving your head doesn't move the bubble.
- **Head nudge:** when your eyes rest on a spot, the bubble turns yellow and holds, and turning or tilting your head pushes it.

| # | Mode | Head correction | Head nudge | What your head does to the bubble | `head.mode` |
|---|------|-----------------|------------|-----------------------------------|-------------|
| 1 | **Eyes only** (default) | on | off | nothing | `cancel` |
| 2 | **Eyes + head nudge** | on | on | moves it only on purpose, while it's yellow | `both` |
| 3 | **Eyes + head nudge, no head correction** | off | on | moves it all the time: by accident while it's white, on purpose while it's yellow | `nudge` |

Mode 1 is how mouSEE worked before the head modes. The names in `head.mode` are from before the modes were renamed, and stay so saved settings keep working.

### The difference between 2 and 3

Once the bubble is yellow, they work the same. The difference is while it's white and following your eyes. Say you're looking at a button and lean a little to the left without meaning to:

- **mode 2:** the head correction undoes the lean, so the bubble stays on the button
- **mode 3:** nothing undoes it, so the bubble slides off the button. And while it's held, turning your head shifts the eye reading a bit too, so it lets go by itself more often

Mode 3 is there to compare against: if it feels steadier than mode 2 for you, the head correction is making things worse, and that's worth knowing.

## The two switches

Each switch lives in one place.

### Head correction: moving your head doesn't move the bubble

The calibration measures what leaning and tilting your head do to the eye readings (the head step, see [GAZE-FIT.md](../calibration/GAZE-FIT.md#moving-your-head-the-head-step)) and takes that back out, so the bubble stays where you look however your head moves.

- **where:** in the tracker, `estimate_gaze()` in tracker/gaze_model.py with the face position and head pitch from `head_inputs()` in tracker/face_tracker.py
- **turned off by:** `--no-head-compensation`, which the app passes in mode 3 (app/Settings/TrackerArgs.cs). Switching to or from mode 3 restarts the tracker, which takes a few seconds

### Head nudge: the eyes get it close, the head does the last bit

1. **Following:** the bubble is where your eyes are.
2. **Holding:** once your eyes stay on one spot for `head.nudgeSettleMs` (600 ms, it was 300 and held before the eyes had got there), the bubble holds there and turns **yellow**. Your head angle at that moment counts as zero.
3. **Nudging:** while it holds, turn your head right or left to move it right or left, tilt it down or up to move it down or up. Each degree past the dead zone (`head.nudgeDeadZone`, 1°) moves it `head.nudgeStrength` (1.5%) of the screen, at most 35% either way.
4. **Letting go:** look somewhere else (more than `head.nudgeReleasePercent`, 20% of the screen, from both the held spot and where the nudged bubble is now, for 0.15 s) and the bubble follows your eyes again, back to white. Looking at the yellow bubble after nudging it keeps it held: that used to count as looking away from the held spot and let go.

While the bubble is held (yellow), and for 2 s after it lets go, the overlay's where-to-sit tips ("Tilt your head up a little"...) don't show: the turning and tilting is on purpose then, and the 2 s are to get your head back (`QuietGuide` in app/UI/OverlayWindow.xaml.cs).

The settle time, strength, let-go distance and dead zone are sliders in the calibrations window, under the modes, when mode 2 or 3 is picked (`SettingSlider`, app/UI/SettingSlider.cs).

So to reach the bottom left corner when the bubble stops short: look at the corner, wait for the bubble to turn yellow, then turn and tilt your head a little toward the corner.

It's measured from the moment it holds, not from how you sat during calibration, so posture doesn't push the bubble around. The recordings show why it can tell a nudge from looking around: sitting still, the head angles move 0.1-0.2° from frame to frame. Looking all over the screen turns the head less than 1° and tilts it about 2°. Leaning or tilting on purpose is 6-11°.

- **where:** `HeadNudge` in app/Bubble/HeadModes/HeadNudge.cs, used by the overlay's bubble (`UpdateGazeBubble` in app/UI/OverlayWindow.xaml.cs)
- **direction:** the tracker's yaw goes up when you turn to your right and pitch goes up when you tilt down, which is right and down on the screen

## Which mode for what

- **1, Eyes only:** the bubble should land where you look whatever your head does. Good when the calibration is accurate. When it isn't, there's nothing you can do about a spot it doesn't reach.
- **2, Eyes + head nudge:** the head correction keeps the bubble steady while it follows your eyes, and the nudge covers what the calibration misses. Probably the best of the three, which is what trying them is for.
- **3, Eyes + head nudge, no head correction:** you can still reach any spot by nudging, but leaning moves the bubble while it follows your eyes, and nudging moves the eye reading a bit too (your eyes turn the other way to stay on the spot). If the bubble lets go while you nudge, raise `head.nudgeReleasePercent`. Mostly for comparing with 2.

## Comparing them

- **the accuracy test** measures the eyes, not the nudge (it hides the bubble, so there's nothing to nudge). Its report saves the mode it ran in (`headMode`). In mode 3 the leaning and tilting parts show how far head movement moves the bubble without the head correction, so they're much further off. That's expected, not a worse calibration
- **the fine-tune** saves the mode it was made in (`fineTuneReport.headMode`). Sitting still the modes land nearly the same, so one fine-tune works for all three, but it's most accurate in the mode it was made in
- **the nudge** is best judged by using it: how quickly and how calmly you can get the bubble onto small things, like a close button

## Files

| What | Where |
|------|-------|
| the three modes and which parts each turns on | app/Bubble/HeadModes/HeadMode.cs |
| the nudge | app/Bubble/HeadModes/HeadNudge.cs |
| the settings (`head.*`) | `HeadSettings` in app/Settings/AppSettings.cs |
| turning the head correction off (mode 3) | app/Settings/TrackerArgs.cs, `head_inputs()` in tracker/face_tracker.py |
| the bubble (fine-tune, re-center, then nudge) | `UpdateGazeBubble` in app/UI/OverlayWindow.xaml.cs |
| picking the mode | **What your head does** in app/Calibration/Saving/CalibrationListWindow.xaml(.cs) |
| tests | tests/app.Tests/Bubble/HeadModes/ (`HeadModeTests.cs`, `HeadNudgeTests.cs`), `HeadInputsTest` in tests/test_face_tracker.py |
