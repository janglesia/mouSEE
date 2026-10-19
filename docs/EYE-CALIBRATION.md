# Eye check (calibration wizard) design

Notes for building a wizard that tunes the eye detection to each person. It's built now, see [How it's built](#how-its-built) at the end for where the code is and where it differs from these notes.

## Why

Detecting closed, winking and covered eyes works by comparing measurements against fixed limits (`tracker/eye_extractor.py`, `tracker/tracking_status.py`). Those limits were picked from short recordings of **one person in one room**. They depend on:

- the person's eyes (eye shape, lashes, iris colour, glasses)
- the lighting (the same eye read 0.27 in one recording and 0.39 a few hours later)
- where the camera sits (resting head pitch was about -10° with the camera above the screen)

Blink-to-click (`click_method = 'blink'` in the Settings table) needs this to be reliable, so it's worth tuning per person.

The wizard does what the recordings did: ask the person to do specific things (close one eye, close both, tilt their head...) while recording the tracker's numbers, then pick limits that separate the cases.

**Nothing in it needs hands.** mouSEE is for people who can't use their hands, so the person only uses their eyes and head. That's why covering an eye isn't a step and `COVERED_CONTRAST` isn't tuned, see below.

This is a different thing from the nine-point gaze calibration in `app/UI/CalibrationWindow`. That one maps where you look to the screen. Run the eye check first: gaze calibration needs to know when the eyes are closed so it can throw those samples away.

## What gets tuned

| Value | File | Default | What it decides | Tune? |
|---|---|---|---|---|
| `IRIS_HIDDEN_RATIO` | eye_extractor.py | 0.65 | eye closed: no dark iris visible | **yes, most important** |
| `LIDS_OPEN_OPENNESS` | eye_extractor.py | 0.26 | eye open whatever the iris says: lids at least this far apart. Stops reflections on glasses looking like closed eyes | **yes** |
| `COVERED_CONTRAST` | eye_extractor.py | 0.15 | eye covered: almost no contrast in the eye area | no, measuring it needs a hand over the eye. The default still catches hair or something else in front of an eye |
| `MAX_PITCH`, `MAX_YAW` | tracking_status.py | 30°, 35° | head turned too far | **yes**, see "Head angle" below |
| `MIN_EYE_WIDTH_PX` | tracking_status.py | 15 px | too far from the camera | no, but check it (step 0) |
| `CLOSED_BLINK`, `CLOSED_OPENNESS`, `WINK_OPENNESS_RATIO`, `WINK_BLINK_DIFF` | eye_extractor.py | 0.5, 0.15, 0.8, 0.08 | closed/wink when there's no camera image | optional, only a fallback now |
| validator values (`MAX_EYE_WIDTH_JUMP`, ...) | eye_extractor.py | | glitch filter | no, not person-specific |
| `LostAfterMs`, `RecoverFrames` | app/Gaze/TrackingMonitor.cs | 500 ms, 10 | how quickly the status bar reacts | no, a user setting if anything |

## What the tracker already sends

Everything the wizard needs is already in every `frame` message. Per eye (`faces[0].eyes.left` / `.right`):

| Field | Meaning | Used for |
|---|---|---|
| `iris_ratio` | iris brightness / rest of the eye. open ~0.1-0.5, closed ~0.7-1.1 | `IRIS_HIDDEN_RATIO` |
| `contrast` | std / mean brightness inside the eye. open ~0.45-0.7, closed ~0.2-0.66, covered ~0.02-0.07 | `COVERED_CONTRAST` |
| `openness` | lid gap / eye width | fallback wink rule |
| `blink` | MediaPipe blink score 0-1 | fallback rules |
| `width_px` | eye width in pixels | distance check |
| `closed`, `covered` | current decision | checking the result |

Plus `faces[0].pose` (yaw, pitch, roll), top-level `tracking` and `capture_fps`.

`EyeData` in `app/TrackerMessage.cs` reads all of them (`IrisRatio`, `Contrast`, `Blink`, `Openness`, `WidthPx`, `Closed`, `Covered`). `IrisRatio` and `Contrast` are null when the tracker has no image, `Blink` without blendshapes.

## Steps

Each step: say the instruction out loud, wait, record. **Throw away the first ~1.2 s of every step**: in the recordings it took about 1.1 s to react.

**Instructions have to be spoken, not just shown.** With one eye closed, both closed or the head tilted back, people can't read the screen. In the recordings the tester fell a whole step behind because of this. Speak the instruction (e.g. `System.Speech.Synthesis`, NuGet package `System.Speech`), and play a short sound when a step ends ("ding, open your eyes").

| # | Say | Time | Records | For | Check they did it |
|---|---|---|---|---|---|
| 0 | "Sit how you normally would and look at the middle of the screen" | 4 s | pose, `width_px`, `capture_fps` | resting head angle, distance check | face found in most frames |
| 1 | "Keep both eyes open and look at each corner of the screen" (move a dot around) | 8 s | `iris_ratio`, `contrast` of both eyes | open-eye range | looking down partly covers the iris with the lid, so this step matters |
| 2 | "Close your left eye" | 5 s | left: closed values. right: open values | `IRIS_HIDDEN_RATIO` | left `blink` higher than right |
| 3 | "Close your right eye" | 5 s | same, swapped | `IRIS_HIDDEN_RATIO` | right `blink` higher than left |
| 4 | "Close your left eye and tilt your head up. Keep looking at the middle of the screen" | 5 s | left closed values with the head tilted | `IRIS_HIDDEN_RATIO` | pitch at least 15° above resting |
| 5 | "Close your right eye and tilt your head down. Keep looking at the middle of the screen" | 5 s | same | `IRIS_HIDDEN_RATIO` | pitch at least 15° below resting |
| 6 | "Close both eyes" | 4 s | both closed | `IRIS_HIDDEN_RATIO` | both `blink` > 0.5 |
| 7 | Check: random mix of "close left / close right / open both", 3 s each, using the new values | ~20 s | `closed` | confirm it works | see "Checking the result" |

If a step's check fails (e.g. the person didn't tilt their head), repeat that step rather than calculating with bad data.

Closing one eye usually makes the other one squint a bit. Don't count the "open" eye from steps 2-5 as an open sample, use step 1 for open values.

## Turning recordings into values

Use percentiles, not min/max, so a single glitched frame doesn't move the limit.

### `IRIS_HIDDEN_RATIO`

```
open   = iris_ratio of both eyes in step 1
closed = iris_ratio of the closed eye(s) in steps 2-6

open_high  = 99th percentile of open
closed_low = 5th percentile of closed
gap = closed_low - open_high

if gap < 0.1:   failed, keep the old value and tell the person (see below)
value = (open_high + closed_low) / 2, clamped to 0.5 - 0.85
```

### `LIDS_OPEN_OPENNESS`

Added after testing with glasses: a bright screen (the yellow nine-point screen, a white web page) reflects on the lenses right over the iris, the iris ratio of an open eye goes above `IRIS_HIDDEN_RATIO`, and both eyes counted as closed. Without glasses it worked. The lid landmarks don't care about reflections, so now an eye only counts as closed from the iris check if its lids are also less than `LIDS_OPEN_OPENNESS` apart.

```
open   = openness of both eyes in step 1 (on the bright background, looking down included)
closed = openness of the closed eye(s) in steps 2-6

open_low    = 1st percentile of open
closed_high = 99th percentile of closed
gap = open_low - closed_high

if gap < 0.02:  failed, keep the old value (0.26)
value = (open_low + closed_high) / 2, clamped to 0.12 - 0.4
```

The extremes on both sides because a value above a closed eye misses winks, and one below an open eye (looking down brings the upper lid down) lets reflections through again. Reference numbers: open 0.31-0.40 (0.27 head tilted up), closed eye of a wink 0.16-0.24.

### `COVERED_CONTRAST` (not used)

Not tuned, it needs the person to cover an eye with their hand. Kept here in case a caregiver-assisted version is wanted later:

```
covered     = contrast of an eye covered by a hand
not_covered = contrast of the open eyes in step 1 AND the closed eyes in steps 2-6
              (a closed eye must not count as covered, otherwise winks stop working)

covered_high = 99th percentile of covered
other_low    = 1st percentile of not_covered
gap = other_low - covered_high

if gap < 0.05:  failed, keep the old value
value = (covered_high + other_low) / 2, clamped to 0.08 - 0.3
```

### Head angle

Right now `tracking_status.py` checks `abs(pitch) > MAX_PITCH`, which assumes 0° is "facing the screen". It isn't when the camera is above or below the screen. Change it to compare against the resting angle from step 0:

```python
REST_PITCH = 0.0   # from step 0
REST_YAW = 0.0
...
if abs(pose["yaw"] - REST_YAW) > MAX_YAW or abs(pose["pitch"] - REST_PITCH) > MAX_PITCH:
```

From step 1, `MAX_YAW` / `MAX_PITCH` must be bigger than the largest angle (from resting) the person used while looking at the corners, plus about 10°. Otherwise looking at a corner of the screen would count as "head turned". Keep the defaults if those are bigger.

### Distance

Not tuned, but warn in step 0 if the median `width_px` is under ~2x `MIN_EYE_WIDTH_PX` (30 px): "Move closer or use a higher camera resolution". Also warn if `capture_fps` is under ~25 ("not enough light" or "camera too slow").

### Fallback values (optional)

Only used when there's no camera image. If someone wants them tuned anyway, the same steps work: `WINK_OPENNESS_RATIO` from `openness` of the closed eye / open eye in steps 2-3, `WINK_BLINK_DIFF` from the `blink` difference. Those break when the head tilts, which is why the image check replaced them.

## When it fails

A gap that's too small means the measurement can't tell the cases apart for this person in this light. Keep the defaults and tell them what to try:

- `IRIS_HIDDEN_RATIO` failed: "Try more light on your face" (dim light makes the iris and the white of the eye look alike), "Glasses can reflect light, try tilting the screen or the glasses". With glasses this is common, the reflection of the screen lands on the iris. That's fine as long as `LIDS_OPEN_OPENNESS` worked, the lids then decide.
- `LIDS_OPEN_OPENNESS` failed: "Close your eyes all the way", or the camera is too far away to see the lids well.

## Checking the result (step 7)

Run the random mix with the new values and count how often `closed` matches what was asked, after the 1.2 s reaction time. An eye wrongly marked `covered` counts as a miss. Under ~90% for any case: offer to redo the wizard or keep the defaults.

## Saving and loading

The values are saved in the calibration they were made for, under `"eyes"`, with the same names as the constants (see [SAVED-CALIBRATIONS.md](SAVED-CALIBRATIONS.md)). Values the eye check couldn't work out are left out, so the tracker keeps its default for them:

```json
"eyes": {"IRIS_HIDDEN_RATIO": 0.62, "LIDS_OPEN_OPENNESS": 0.27, "REST_PITCH": -9.5, "REST_YAW": 2.0, "MAX_PITCH": 30, "MAX_YAW": 35}
```

The values are module-level constants, and `extract_eyes()` / `tracking_status()` read them each time they run, so setting them at startup is enough. The app starts the tracker with `--calibration <file of the calibration in use>`, and `load_calibration()` in `face_tracker.py` sets each one with `apply_eye_thresholds()`. Only the names in `EYE_THRESHOLDS` are set, and a value outside a sane range (e.g. `IRIS_HIDDEN_RATIO` of 5) is ignored as a broken file.

Suggest redoing the check when the lighting changes a lot (daytime vs evening): the iris ratio of the same eye moved from 0.27 to 0.39 within a few hours in testing. Making a new calibration and switching between them (Ctrl+Alt+C) is how that works now.

## Reference numbers

From the recordings used to pick the defaults (one person, 720p, indoor light). Good for checking a wizard's results look sane.

| | `iris_ratio` | `contrast` | `blink` | `openness` |
|---|---|---|---|---|
| Open, head straight | 0.21-0.33 | 0.45-0.7 | 0.01-0.12 | 0.31-0.40 |
| Open, head tilted up (~-33°) | 0.30 (99th pct 0.49) | | 0.14-0.18 | 0.27 |
| Wink, closed eye, head straight | 0.93-0.97 | 0.2-0.66 | 0.2-0.34 | 0.17-0.24 |
| Wink, closed eye, head tilted up (~-40°) | 0.83-0.91 (5th pct 0.71) | | 0.37 (the open eye: 0.38) | 0.16 |
| Both closed | | | 0.67-0.76 | 0.02-0.03 |
| Covered by a hand | 0.94-1.01 | 0.02-0.07 | low | normal (MediaPipe guesses the eye) |
| Resting head pitch (camera above screen) | | | | -8 to -10° |

## How it's built

Run it with **New calibration** in the calibrations window (Ctrl+Alt+C), **Calibrate now** on the first-run question, or `make run ARGS="--calibrate"`. It's the first half of a new calibration: when it's done, the nine-point gaze screen follows and adds to the same saved calibration.

| File | What's in it |
|------|--------------|
| `app/Calibration/EyeCheckSteps.cs` | the steps (what to say, how long), how each one is checked, the live hints |
| `app/Calibration/EyeCheckAnalysis.cs` | turning the recordings into values, the formulas above |
| `app/Calibration/EyeCheckRound.cs` | step 7, the random mix and its scoring |
| `app/UI/EyeCheckWindow.xaml(.cs)` | runs it: says each step, records frames, repeats steps, shows the result |
| `app/UI/Speaker.cs` | spoken instructions (`System.Speech`) and the sound at the end of a step |
| `app/Calibration/CalibrationReport.cs` | what gets saved: the report for each value and step, and the raw frames |
| `app/Gaze/LookChoice.cs`, `app/UI/ChoicePrompt.xaml(.cs)` | answering Begin / Continue / Do it again by looking right or left |
| `tracker/face_tracker.py` | `load_calibration()`, `apply_eye_thresholds()` |
| `tracker/tracking_status.py` | `REST_PITCH`, `REST_YAW`, head turns measured from them |
| `tracker/eye_extractor.py` | `LIDS_OPEN_OPENNESS`, the lid check on top of the iris check |

Everything except the windows is plain code with tests (`tests/app.Tests/EyeCheckTests.cs`, `tests/app.Tests/CalibrationReportTests.cs`, `tests/test_face_tracker.py`, `tests/test_tracking_status.py`).

No hands needed to run it: the intro says out loud to look right to begin or left to cancel, and the result screen works the same way (right to continue, left to do it again). See [Answering by looking](SAVED-CALIBRATIONS.md#answering-by-looking).

What's saved: for every value, what it's set to, the default, the numbers it was worked out from and a plain explanation with them (the result screen shows these). For every step, what it measured (spread of each number per eye, head angle, fps, tries). And every recorded frame in a separate file, so the math can be run again on it later. The format is in [SAVED-CALIBRATIONS.md](SAVED-CALIBRATIONS.md#the-files).

How a step runs: the instruction is shown and spoken, recording starts once it's been said, the first 1.2 s are thrown away, and a sound plays at the end. While it records, the last 0.8 s are checked with the same rules as the end of the step. If something's off ("Tilt your head further up", "Close your left eye all the way") it's shown, and the first one is said out loud. At the end the step is checked. If the check fails, the problem is said and the step repeats, up to 3 tries. If steps 0 or 1 still fail the eye check stops, since everything after compares against them. A later step that keeps failing is left out, and the values that need it keep their defaults.

Where it differs from the notes above:

- **No covering an eye**: the notes had two steps (cover left / cover right with a hand) and a "cover left" in the check. They're left out because mouSEE is for people who can't use their hands. `COVERED_CONTRAST` keeps its default.
- **Bright background for step 1 and step 7**: they run on the nine-point screen's yellow instead of the dark background, so the open-eye measurements include what glasses reflect from a bright screen. The other steps stay dark: the closed-eye ones are fine either way, and it's easier on the eyes.
- **Closed-eye check (steps 2-5)**: the closed eye has to have a blink score at least 0.05 higher than the open one *or* an iris ratio at least 0.15 higher. Blink alone fails with the head tilted, where both eyes read ~0.37 (see the reference numbers). It also fails the step if the "open" eye's blink score is over 0.5, i.e. both eyes were closed.
- **Step 1 check**: both eyes measured (iris ratio) in 70% of the frames, and neither eye's median blink score over 0.5.
- **Tilt direction**: MediaPipe's pitch gets more negative tilting up (resting -10, tilted up -33 in the recordings). Steps 4 and 5 need at least 15° from resting in the asked direction.
- **Every step** needs the face in 80% of its frames, and at least 20 frames after the reaction time.
- **Percentiles of the head angle**: `MAX_PITCH` / `MAX_YAW` use the 99th percentile of how far the head turned in step 1 (from resting) plus 10°, not the single largest frame, so one glitch doesn't raise them.
- **Step 7**: each of the three instructions twice (6 × 3 s), shuffled so the same one never comes twice in a row. It's only run if `IRIS_HIDDEN_RATIO` or `LIDS_OPEN_OPENNESS` changed, there's nothing to check otherwise. Before it, the tracker restarts with the new values from a temporary file (the app's `UseTrackerCalibrationAsync`). That takes a few seconds, and afterwards it goes back to the calibration in use.
- **Result**: under 90% for any case offers **Do it again** or **Keep default values**. Keeping the defaults drops `IRIS_HIDDEN_RATIO` and `LIDS_OPEN_OPENNESS` but keeps the head angle values, step 7 only checked the closed eye ones. If steps 0 or 1 failed there's nothing to keep, and the calibration gets no eye values.
- **Saving**: in the calibration file instead of the database, and `--calibration` instead of `--eye-thresholds`, see "Saving and loading".

The fallback values (`WINK_OPENNESS_RATIO`, `WINK_BLINK_DIFF`) aren't tuned.

## Checking a detection problem

`--record-eyes FILE` makes the tracker write every frame's measurements for both eyes to a CSV file: the tracking state, head angle, and per eye `iris_ratio`, `contrast`, `blink`, `openness`, `width_px`, `closed`, `covered`. Record while doing the thing that goes wrong (e.g. looking at the nine-point screen with glasses on), then look at which number crosses its limit:

```bash
make run ARGS="--record-eyes eyes.csv"
```

The file ends up in `tracker/` (that's where the tracker runs). It's plain CSV, open it in Excel or anything else.
