# Eye check (calibration wizard) design

Notes for building a wizard that tunes the eye detection to each person. Nothing here is built yet.

## Why

Detecting closed, winking and covered eyes works by comparing measurements against fixed limits (`tracker/eye_extractor.py`, `tracker/tracking_status.py`). Those limits were picked from short recordings of **one person in one room**. They depend on:

- the person's eyes (eye shape, lashes, iris colour, glasses)
- the lighting (the same eye read 0.27 in one recording and 0.39 a few hours later)
- where the camera sits (resting head pitch was about -10° with the camera above the screen)

Blink-to-click (`click_method = 'blink'` in the Settings table) needs this to be reliable, so it's worth tuning per person.

The wizard does what the recordings did: ask the person to do specific things (close one eye, cover it, tilt their head...) while recording the tracker's numbers, then pick limits that separate the cases.

This is a different thing from the nine-point gaze calibration in `app/UI/CalibrationWindow`. That one maps where you look to the screen. Run the eye check first: gaze calibration needs to know when the eyes are closed so it can throw those samples away.

## What gets tuned

| Value | File | Default | What it decides | Tune? |
|---|---|---|---|---|
| `IRIS_HIDDEN_RATIO` | eye_extractor.py | 0.65 | eye closed: no dark iris visible | **yes, most important** |
| `COVERED_CONTRAST` | eye_extractor.py | 0.15 | eye covered: almost no contrast in the eye area | **yes** |
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

The C# side doesn't read most of these yet: `EyeData` in `app/TrackerMessage.cs` only has `Offset` and `Closed`. Add `Openness`, `Blink`, `Contrast`, `IrisRatio` (`[JsonPropertyName("iris_ratio")]`), `Covered` and `WidthPx` (`width_px`). All can be null when the tracker has no image.

## Steps

Each step: say the instruction out loud, wait, record. **Throw away the first ~1.2 s of every step**: in the recordings it took about 1.1 s to react.

**Instructions have to be spoken, not just shown.** With one eye closed, a hand over an eye or the head tilted back, people can't read the screen. In the recordings the tester fell a whole step behind because of this. Speak the instruction (e.g. `System.Speech.Synthesis`, NuGet package `System.Speech`), and play a short sound when a step ends ("ding, open your eyes").

| # | Say | Time | Records | For | Check they did it |
|---|---|---|---|---|---|
| 0 | "Sit how you normally would and look at the middle of the screen" | 4 s | pose, `width_px`, `capture_fps` | resting head angle, distance check | face found in most frames |
| 1 | "Keep both eyes open and look at each corner of the screen" (move a dot around) | 8 s | `iris_ratio`, `contrast` of both eyes | open-eye range | looking down partly covers the iris with the lid, so this step matters |
| 2 | "Close your left eye" | 5 s | left: closed values. right: open values | `IRIS_HIDDEN_RATIO` | left `blink` higher than right |
| 3 | "Close your right eye" | 5 s | same, swapped | `IRIS_HIDDEN_RATIO` | right `blink` higher than left |
| 4 | "Close your left eye and tilt your head up" | 5 s | left closed values with the head tilted | `IRIS_HIDDEN_RATIO` | pitch at least 15° above resting |
| 5 | "Close your right eye and tilt your head down" | 5 s | same | `IRIS_HIDDEN_RATIO` | pitch at least 15° below resting |
| 6 | "Close both eyes" | 4 s | both closed | `IRIS_HIDDEN_RATIO` | both `blink` > 0.5 |
| 7 | "Cover your left eye with your hand" | 5 s | left covered, right open | `COVERED_CONTRAST` | left `contrast` dropped a lot compared with step 1 |
| 8 | "Cover your right eye with your hand" | 5 s | same, swapped | `COVERED_CONTRAST` | same |
| 9 | Check: random mix of "close left / close right / open both / cover left", 3 s each, using the new values | ~20 s | `closed` / `covered` | confirm it works | see "Checking the result" |

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

### `COVERED_CONTRAST`

```
covered     = contrast of the covered eye in steps 7-8
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

- `IRIS_HIDDEN_RATIO` failed: "Try more light on your face" (dim light makes the iris and the white of the eye look alike), "Glasses can reflect light, try tilting the screen or the glasses".
- `COVERED_CONTRAST` failed: "Cover the eye with your whole hand", or it's very dark.

## Checking the result (step 9)

Run the random mix with the new values and count how often `closed` / `covered` matches what was asked (after the 1.2 s reaction time). Under ~90% for any case: offer to redo the wizard or keep the defaults.

## Saving and loading

Save per user. The Settings table in `persistence/database.py` has one row per user; either add a column per value or one JSON text column, e.g.:

```json
{"IRIS_HIDDEN_RATIO": 0.62, "COVERED_CONTRAST": 0.12, "REST_PITCH": -9.5, "REST_YAW": 2.0, "MAX_PITCH": 30, "MAX_YAW": 35}
```

The tracker needs a way to load them. The values are module-level constants, and `extract_eyes()` / `tracking_status()` read them each time they run, so overriding them at startup is enough. In `face_tracker.py`:

```python
import eye_extractor, tracking_status

parser.add_argument("--eye-thresholds", help="JSON file with calibrated values, see docs/eye-calibration.md")
...
if args.eye_thresholds:
    for name, value in json.load(open(args.eye_thresholds)).items():
        module = eye_extractor if hasattr(eye_extractor, name) else tracking_status
        setattr(module, name, value)
```

The app passes its command line on to the tracker (`TrackerClient.RunAsync`), so it can add `--eye-thresholds <file>` there, writing the user's saved values to a temp file first.

Suggest redoing the check when the lighting changes a lot (daytime vs evening): the iris ratio of the same eye moved from 0.27 to 0.39 within a few hours in testing.

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
