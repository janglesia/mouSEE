# Where to sit

The overlay has a yellow bar under the status bar that tells you two things (`PositionGuide`, app/Bubble/PositionGuide.cs):

- **you moved far from where you sat during calibration**: the fit only knows the head around there, so tracking gets worse. It says which way to go: "Move a little to your left / right", "Sit up a little", "Sit a little lower", "Move back a little, you're too close to the camera", "Move a little closer to the camera", "Tilt your head up / down a little". From 6% of the camera image to the side or up/down (`overlay.positionHintPercent`), the face 1.8 / 1.5 times that bigger or smaller (11% / 9%), or the head tilted 8 degrees (`overlay.tiltHintDegrees`), for at least 1 s. The biggest problem first. It goes away once you're back inside 80% of the limit. Both limits are sliders in the calibrations window, under **Where-to-sit hints**. It was 10% at first with no tilt hint, which let the head get far enough off that the bubble was clearly wrong before it said anything
- **you moved since the last re-center**: smaller moves (5% of the image, the face 8% bigger or smaller, the head tilted 8°) for at least 8 s: "You moved since the last re-center. Press Ctrl+Alt+R..." It goes away when you re-center or move back

Both use the head position over the last 1.5 s (the median), not single frames. The tracker's head pitch jitters by up to 4° sitting still and shifts 3-4° just from looking at the bottom of the screen, which is why tilting needs 8° (it was 4° at first and went off all the time).

Left and right are your own: the camera image is mirrored like everything else in the tracker, so the face moving right in the image means you moved to your right, and the hint says to move left.
