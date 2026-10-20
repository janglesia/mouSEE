# Eye check

The last part of a new calibration: it tunes when your eyes count as closed. It runs right after the grid, and takes its open eyes and your resting head angle from the grid's recording. See [How it's built](#how-its-built) for where the code is.

## Why

Detecting closed and covered eyes works by comparing measurements against fixed limits (`tracker/eye_extractor.py`, `tracker/tracking_status.py`). Those limits were picked from short recordings of **one person in one room**. They depend on:

- the person's eyes (eye shape, lashes, iris colour, glasses)
- the lighting (the same eye read 0.27 in one recording and 0.39 a few hours later)
- where the camera sits (resting head pitch was about -10° with the camera above the screen)

What the app needs is **both eyes closed**: closing your eyes for 1.5 s answers questions ("done looking", `ClosedEyesGate`), and blink-to-click (`click_method = 'blink'`) will use it. Nothing uses one eye closed (a wink).

The eye check asks the person to close both eyes (sitting normally, then with the head tilted up and down) while recording the tracker's numbers, and picks the limit that separates those from the open eyes the grid recorded.

**Nothing in it needs hands.** mouSEE is for people who can't use their hands, so the person only uses their eyes and head. That's why covering an eye isn't a step and `COVERED_CONTRAST` isn't tuned, see below.

## The order: grid first

It used to run before the grid, with two steps of its own for the open eyes: sitting still looking at the middle (4 s, for the resting head angle) and following a dot to the corners (8 s, for the open eyes, and to raise the head turn limits if looking at the corners turned the head a lot). The grid measures both better: open eyes at 20 known places on the screen including the bottom row, and the head angle over the whole grid. And following the dot had two problems:

- it went to the bottom corners, where the lids come down, so the open eyes overlapped the closed ones and the closed eye values couldn't be tuned (iris ratio open up to 0.75, closed from 0.54, in a real calibration)
- the head turn limits could only be raised from 30° / 35°, by how far the head turned plus 10°. People turn their head 1° or less following a dot with their eyes, so it never changed anything

So now the grid runs first, and the eye check only closes the eyes. The grid's targets count frames with closed eyes as unusable by the tracker's default values, which is all it needs (blinks).

## What gets tuned

| Value | File | Default | What it decides | Tuned? |
|---|---|---|---|---|
| `LIDS_SHUT_OPENNESS` | eye_extractor.py | 0.1 | eye closed: lids closer together than this, whatever the iris check says | **yes** |
| `REST_PITCH`, `REST_YAW` | tracking_status.py | 0°, 0° | the head angle sitting normally, head turns are measured from it | **yes**, the median over the grid |
| `IRIS_HIDDEN_RATIO` | eye_extractor.py | 0.65 | eye closed: no dark iris visible (with `LIDS_OPEN_OPENNESS`). What catches a wink | no, needs winks to tune. Older eye checks set it |
| `LIDS_OPEN_OPENNESS` | eye_extractor.py | 0.26 | eye open whatever the iris says: lids at least this far apart. Stops reflections on glasses looking like closed eyes | no, same. Older eye checks set it |
| `COVERED_CONTRAST` | eye_extractor.py | 0.15 | eye covered: almost no contrast in the eye area | no, measuring it needs a hand over the eye |
| `MAX_PITCH`, `MAX_YAW` | tracking_status.py | 30°, 35° | head turned too far | no (see above). Older eye checks saved them, the tracker still reads them |
| `MIN_EYE_WIDTH_PX` | tracking_status.py | 15 px | too far from the camera | no, but warned about |
| `CLOSED_BLINK`, `CLOSED_OPENNESS`, `WINK_OPENNESS_RATIO`, `WINK_BLINK_DIFF` | eye_extractor.py | 0.5, 0.15, 0.8, 0.08 | closed/wink when there's no camera image | no, only a fallback |
| validator values (`MAX_EYE_WIDTH_JUMP`, ...) | eye_extractor.py | | glitch filter | no, not person-specific |

## How the tracker decides "closed"

With a camera image, per eye:

```
closed = (iris_ratio > IRIS_HIDDEN_RATIO and openness < LIDS_OPEN_OPENNESS)   # no iris, lids not wide open: a wink too
         or openness < LIDS_SHUT_OPENNESS                                       # lids shut
```

then the looking down rule (below) can open both eyes again, and a covered eye is never closed.

The lid rule was added with this version of the eye check. With both eyes shut the iris ratio can stay low: 0.51-0.62 in a real calibration (the lashes and the shadow under the lid are dark), under `IRIS_HIDDEN_RATIO`, so the iris check alone caught the left eye closed 36% of the time and the right one never, while the lids were nearly shut (0.04). Open eyes stay above about 0.24 even looking at the bottom of the screen, a winked eye 0.16-0.24, so a lid limit around 0.1-0.15 separates shut eyes from everything else. Winks still need the iris check.

## What the tracker sends

Everything the eye check needs is in every `frame` message. Per eye (`faces[0].eyes.left` / `.right`):

| Field | Meaning | Used for |
|---|---|---|
| `openness` | lid gap / eye width | `LIDS_SHUT_OPENNESS`, checking the eyes closed |
| `blink` | MediaPipe blink score 0-1 | checking the eyes closed, leaving blinks out of the grid's open eyes |
| `iris_ratio` | iris brightness / rest of the eye. open ~0.1-0.5, a winked eye ~0.7-1.1, both shut 0.5-0.6 | the report |
| `contrast` | std / mean brightness inside the eye. open ~0.45-0.7, closed ~0.2-0.66, covered ~0.02-0.07 | the report |
| `width_px` | eye width in pixels | distance warning |
| `closed`, `covered` | current decision | checking the result |

Plus `faces[0].pose` (yaw, pitch, roll), top-level `tracking` and `capture_fps`. `EyeData` in `app/Tracker/TrackerMessage.cs` reads all of them.

## Steps

Each step: say the instruction out loud, wait, record. **The first 1.2 s of every step are thrown away**: in the recordings it took about 1.1 s to react.

**Instructions are spoken, not just shown.** With the eyes closed or the head tilted back, people can't read the screen. A short sound plays when a step ends.

| # | Say | Time | Check they did it |
|---|---|---|---|
| 1 | "Close both eyes until you hear the sound" | 4 s | both eyes closed |
| 2 | "Tilt your head up and look at the middle of the screen", then once it's tilted "Now close both eyes until you hear the sound" | 4 s | both closed, pitch at least 15° above resting |
| 3 | the same with the head tilted down | 4 s | both closed, pitch at least 15° below resting |
| 4 | Check: "Close both eyes" and "Open your eyes", once each, with the new value | ~6 s | see "Checking the result" |

"Both eyes closed" in the checks is MediaPipe's blink score over 0.5 or the lids under 0.12, for each eye (median over the step). It's whether the person did it that's checked, not the tracker's own decision.

The tilt steps are there because tilting the head changes how the lids look to the camera (and lowers or raises the blink score). They say to tilt first and to close the eyes only once the head is far enough, since the live guidance for the tilt can't be read with the eyes closed. They show a dot in the middle of the screen while tilting (`MiddleDot`, see [LOOK-TO-ANSWER.md](../LOOK-TO-ANSWER.md#the-middle-dot)).

If a step's check fails, it's repeated rather than calculated with bad data.

## Turning recordings into values

Percentiles, not min/max, so a single glitched frame doesn't move the limit.

### `LIDS_SHUT_OPENNESS`

```
open   = openness of both eyes in the grid's frames sitting normally (the 20 points, not the head step),
         blinks left out (a frame counts if neither eye is closed and both blink scores are under 0.5)
closed = openness of both eyes in steps 1-3

open_low    = 1st percentile of open
closed_high = 99th percentile of closed
gap = open_low - closed_high

if gap < 0.05:  failed, keep the default (0.1)
value = (open_low + closed_high) / 2, clamped to 0.05 - 0.15
```

The open eyes include the bottom row of the grid on purpose: the lids come down looking there, and the limit has to stay under that. At most 0.15 because that's `LOOKING_DOWN_MIN_OPENNESS`: above it the looking down rule would open an eye the lid rule closed. In practice it usually ends up at 0.15 (open eyes from about 0.24, shut ones up to about 0.05).

### `REST_PITCH`, `REST_YAW`

The median head pitch and yaw over the grid's frames sitting normally.

`tracking_status.py` measures head turns from the resting angle (`abs(pitch - REST_PITCH) > MAX_PITCH`), since 0° is facing the camera, which isn't facing the screen when the camera is above or below it.

### Looking down is not closed eyes

Looking at the bottom of the screen brings both lids down about as far as the closed eye of a wink (0.25 in a recording, a wink 0.16-0.24), and the lid covers part of the iris, so both eyes read as closed by the iris check. The bottom row of the calibration had no usable frames, and the bubble had nothing to go on there. No single lid limit fixes that for winks: an old eye check tuned `LIDS_OPEN_OPENNESS` to 0.246 and then the right-eye wink was only caught 62% of the time.

What tells them apart is that a wink is one eye and looking down is both, equally. In the recording the two lids were within 16% of each other looking anywhere with the eyes open (smaller / bigger 0.84-1.0, the bottom row included) and at most 0.80 for a wink (0.57-0.80). Both eyes closed has the lids nearly shut (0.04) and a high blink score (0.73). So in `eye_extractor.py`: if one or both eyes read as closed, but the lids are alike (`SAME_LIDS_RATIO` 0.82), not shut (`LOOKING_DOWN_MIN_OPENNESS` 0.15) and the blink score is low (`LOOKING_DOWN_MAX_BLINK` 0.45), both eyes count as open and get `"looking_down": true`. Replayed on that recording: the bottom row went from 0 and 52 usable frames to all of them, and none of the 1097 frames with really closed eyes (winks, both closed) was opened.

The gaze fit applies the same rule to recordings made before it (`GazeTargetSummary.LooksDown`, fit version 4), so older calibrations get their bottom row back too. These are fixed numbers, not tuned per person.

### The wink values (not tuned)

`IRIS_HIDDEN_RATIO` and `LIDS_OPEN_OPENNESS` decide when one eye is closed, and stop reflections on glasses from looking like a closed eye: a bright screen (the yellow grid screen, a white web page) reflects on the lenses right over the iris, the iris ratio of an open eye goes above `IRIS_HIDDEN_RATIO`, and it only counts as closed if the lids are also less than `LIDS_OPEN_OPENNESS` apart. The old eye check tuned them from four wink steps. Calibrations made with it still have them in their file, and the tracker uses them.

### `COVERED_CONTRAST` (not tuned)

It needs the person to cover an eye with their hand. Kept here in case a caregiver-assisted version is wanted later:

```
covered     = contrast of an eye covered by a hand
not_covered = contrast of the open eyes (the grid) AND the closed eyes (steps 1-3)

covered_high = 99th percentile of covered
other_low    = 1st percentile of not_covered
gap = other_low - covered_high

if gap < 0.05:  failed, keep the old value
value = (covered_high + other_low) / 2, clamped to 0.08 - 0.3
```

### Distance

Not tuned, but it warns if the median `width_px` over the grid is under ~2x `MIN_EYE_WIDTH_PX` (30 px): "Move closer or use a higher camera resolution". Also if `capture_fps` is under ~25 ("not enough light" or "camera too slow").

## When it fails

A gap that's too small means the lids can't tell open from shut for this person in this light, usually because the eyes weren't closed all the way. It keeps the default and says so. The report shows what was measured.

## Checking the result (step 4)

Only if `LIDS_SHUT_OPENNESS` changed. The tracker restarts with the new value from a temporary file (the app's `UseTrackerCalibrationAsync`), which takes a few seconds, then "Close both eyes" and "Open your eyes", once each (3 s each, in either order, about 100 frames each after the reaction time; it was 3 times each, which took longer without telling more), on the grid screen's yellow (so the open eyes include what glasses reflect from a bright screen). It counts how often the tracker's `closed` matches what was asked for both eyes, after the 1.2 s reaction time. An eye wrongly marked `covered` counts as a miss. Under 90% for either: offer to do it again or keep the default. Afterwards the tracker goes back to the calibration in use.

While a check runs, a green "Good, both eyes closed" (or "open") shows when the tracker sees what was asked in most of the last moment (`EyeCheckRound.LiveGood`). It doesn't say what's wrong: getting it wrong is what the check measures, and a hint would change it.

## Saving and loading

The values are saved in the calibration they were made for, under `"eyes"`, with the same names as the constants (see [SAVED-CALIBRATIONS.md](SAVED-CALIBRATIONS.md)). Values the eye check couldn't work out are left out, so the tracker keeps its default for them:

```json
"eyes": {"LIDS_SHUT_OPENNESS": 0.15, "REST_PITCH": -13.6, "REST_YAW": 2.4}
```

The values are module-level constants, and `extract_eyes()` / `tracking_status()` read them each time they run, so setting them at startup is enough. The app starts the tracker with `--calibration <file of the calibration in use>`, and `load_calibration()` in `face_tracker.py` sets each one with `apply_eye_thresholds()`. Only the names in `EYE_THRESHOLDS` are set, and a value outside a sane range (e.g. `LIDS_SHUT_OPENNESS` of 0.5) is ignored as a broken file.

Suggest redoing the calibration when the lighting changes a lot (daytime vs evening): the iris ratio of the same eye moved from 0.27 to 0.39 within a few hours in testing. Making a new calibration and switching between them (Ctrl+Alt+C) is how that works now.

## Reference numbers

From the recordings used to pick the defaults (one person, 720p and 1080p, indoor light). Good for checking the results look sane.

| | `iris_ratio` | `contrast` | `blink` | `openness` |
|---|---|---|---|---|
| Open, head straight | 0.21-0.33 | 0.45-0.7 | 0.01-0.12 | 0.31-0.40 |
| Open, looking at the bottom row | | | | 0.25-0.26 |
| Open, head tilted up (~-33°) | 0.30 (99th pct 0.49) | | 0.14-0.18 | 0.27 |
| Wink, closed eye, head straight | 0.93-0.97 | 0.2-0.66 | 0.2-0.34 | 0.17-0.24 |
| Wink, closed eye, head tilted up (~-40°) | 0.83-0.91 (5th pct 0.71) | | 0.37 (the open eye: 0.38) | 0.16 |
| Both closed | 0.51-0.62 | 0.36-0.56 | 0.67-0.76 | 0.02-0.05 |
| Covered by a hand | 0.94-1.01 | 0.02-0.07 | low | normal (MediaPipe guesses the eye) |
| Resting head pitch (camera above screen) | | | | -8 to -14° |

## How it's built

A new calibration (**New calibration** in the calibrations window, Ctrl+Alt+C, **Calibrate now** on the first-run question, or `make run ARGS="--calibrate"`) is the grid, then this. `App.StartCalibration()` opens the grid; once the grid is kept and its window closes, `App.StartEyeCheck()` opens the eye check with the grid's frames from the recording, and its values are added to the same calibration. Skipping it (looking left at its start, or Esc) leaves the calibration with the default eye values.

| File | What's in it |
|------|--------------|
| `app/Calibration/EyeCheck/EyeCheckSteps.cs` | the steps (what to say, how long), how each one is checked, the live hints, the resting angle from the grid |
| `app/Calibration/EyeCheck/EyeCheckAnalysis.cs` | turning the grid's open eyes and the closed steps into values, the formulas above |
| `app/Calibration/EyeCheck/EyeCheckRound.cs` | step 4, closed and open once each, and its scoring |
| `app/Calibration/EyeCheck/EyeCheckWindow.xaml(.cs)` | runs it: says each step, records frames, repeats steps, shows the result |
| `app/UI/Speaker.cs` | spoken instructions (`System.Speech`) and the sound at the end of a step |
| `app/Calibration/EyeCheck/EyeCheckReport.cs` | what gets saved: the report for each value and step, and the raw frames of each step |
| `app/LookToAnswer/LookChoice.cs`, `app/LookToAnswer/ChoicePrompt.xaml(.cs)` | answering Begin / Continue / Do it again by looking right or left |
| `tracker/face_tracker.py` | `load_calibration()`, `apply_eye_thresholds()` |
| `tracker/tracking_status.py` | `REST_PITCH`, `REST_YAW`, head turns measured from them |
| `tracker/eye_extractor.py` | `LIDS_SHUT_OPENNESS`, `LIDS_OPEN_OPENNESS`, the lid checks around the iris check |

Everything except the window is plain code with tests (`tests/app.Tests/Calibration/EyeCheck/`, `tests/test_eye_extractor.py`, `tests/test_face_tracker.py`, `tests/test_tracking_status.py`).

No hands needed to run it: the intro says out loud to look right to begin or left to skip, and the result screen works the same way (right to continue, left to do it again). See [Answering by looking](../LOOK-TO-ANSWER.md).

What's saved: for every value, what it's set to, the default, the numbers it was worked out from and a plain explanation with them (the result screen shows these). For every step, what it measured (spread of each number per eye, head angle, fps, tries). And every recorded frame in the calibration's recording, so the math can be run again on it later. The format is in [SAVED-CALIBRATIONS.md](SAVED-CALIBRATIONS.md#the-files).

How a step runs: the instruction is shown and spoken, recording starts once it's been said, the first 1.2 s are thrown away, and a sound plays at the end. While it records, the last 0.8 s are checked with the same rules as the end of the step; if something's off ("Keep both eyes closed until you hear the sound") it's shown, and said out loud once. At the end the step is checked. If the check fails, the problem is said and the step repeats, up to 3 tries. A step that keeps failing is left out, and the value is worked out from the others (or keeps its default if none worked).

The two tilt steps are guided live, like the grid's head step: from the moment the instruction starts being read out, a line under it shows how far your head is tilted from resting against the 15° it needs ("Tilt your head up a little more (9° of 15°)", "A little less" past 28°, since from 30° the head counts as turned away, and in green "Good, hold it there (18°)"). Once the head has been far enough for 0.8 s it says to close the eyes, and recording starts. If the head isn't there after 3 s the hint is said out loud once, and after 12 s it goes on anyway (and the step's check says if it wasn't enough). `EyeCheckSteps.TiltGuide` works it out, `EyeCheckWindow.WaitForTiltAsync` waits for it.

The result: under 90% in the check round offers **Do it again** or **Keep default value**. Keeping the default drops `LIDS_SHUT_OPENNESS` but keeps the head angle values, the check round only checked the closed eyes.

## Checking a detection problem

`--record-eyes FILE` makes the tracker write every frame's measurements for both eyes to a CSV file: the tracking state, head angle, and per eye `iris_ratio`, `contrast`, `blink`, `openness`, `width_px`, `closed`, `covered`. Record while doing the thing that goes wrong (e.g. looking at the grid screen with glasses on), then look at which number crosses its limit:

```bash
make run ARGS="--record-eyes eyes.csv"
```

The file ends up in `tracker/` (that's where the tracker runs). It's plain CSV, open it in Excel or anything else.
