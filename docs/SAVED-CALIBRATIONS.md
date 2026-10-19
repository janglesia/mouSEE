# Saved calibrations

How mouSEE keeps calibrations, picks which one to use, and what's in the files.

A calibration has two parts. The **eye check** comes first: how your eyes look open and closed, and how you hold your head (see [EYE-CALIBRATION.md](EYE-CALIBRATION.md)). That part works and its values are used by the tracker. Then the **nine-point gaze screen**, for where on the screen you look. Working out its numbers isn't built yet, so the gaze part is saved empty and the gaze uses default values.

## How it works

- Every calibration is its own file in `app/Calibration/Saved/`. The folder is in `.gitignore`, everyone has their own.
- The one you used last is the one in use. A new calibration is used straight away, and picking an older one switches to it. It's still the one in use next time mouSEE starts.
- No calibrations: mouSEE uses the default values: `GazeParams` in `tracker/gaze_model.py` for the gaze, which assume you're straight in front of the middle of the screen, and the constants in `eye_extractor.py` / `tracking_status.py` for closed, covered and head turned.
- Deleting the one in use switches to the one you used before it, or back to the default values if it was the only one.

## On startup

If there are no saved calibrations, mouSEE asks if you want to calibrate, out loud too. You don't need your hands to answer: **look to the right** and hold it for 3 seconds for yes, **to the left** for no. A bar fills up on that side while you hold it. The buttons work as well. See [Answering by looking](#answering-by-looking).

- **Calibrate now** (or look right) starts a calibration: the eye check, then the nine-point screen. It's saved and used as soon as the eye check is done, the nine-point screen adds to it.
- **Use default values** (or look left) closes the question. It asks again next time.
- Untick **Ask me to calibrate when mouSEE starts without a calibration** to stop the question. That's the `calibration.askWhenMissing` setting (see [SETTINGS.md](SETTINGS.md)), turn it back on there or tick the box again in the calibrations window.

Once there's at least one calibration it never asks, it just uses the last one.

## The calibrations window

Open it with **Ctrl+Alt+C** (from any program) or **c** in the camera window.

- the list shows every saved calibration, newest used first, with when it was made. The one in use says IN USE
- **Use this one** (or double-click) switches to the selected one
- **Delete** deletes the selected one, after asking
- **Test accuracy** opens the accuracy test with the calibration in use, see [Testing accuracy](#testing-accuracy)
- **New calibration** starts a new one (eye check, then the nine-point screen), it's saved and used once the eye check is done
- if a calibration was made on a screen with a different size, it says so. It still works but won't line up

Switching restarts the tracker with the new calibration, which takes a few seconds (the status bar says "Starting camera...").

`make run ARGS="--calibrate"` starts a new calibration straight away. `make run ARGS="--calibration-preview"` opens only the nine-point screen, without the overlay. Accepting there saves a calibration with no eye values.

## The files

Each calibration is two files:

- `app/Calibration/Saved/2026-10-18_14-05-30.json`: the calibration. What the tracker uses (`gaze`, `eyes`), plus a summary of everything that was measured and why each value is what it is. Readable, a few hundred lines
- `app/Calibration/Saved/recordings/2026-10-18_14-05-30.json`: every frame that was recorded, see [The recording](#the-recording). Big (around 1 MB), one line

The file name is when it was made, so renaming a calibration doesn't move the files.

### What "default" means

The defaults are the values built into the tracker (`eye_extractor.py`, `tracking_status.py`, `gaze_model.py`), picked by hand from short recordings of one person in one room. A calibration replaces the ones it can measure for you. The rest stay at the default, and the report says why.

| Value | Default | What it decides |
|-------|---------|-----------------|
| `IRIS_HIDDEN_RATIO` | 0.65 | an eye counts as closed when its iris looks hidden (iris ratio above this) |
| `LIDS_OPEN_OPENNESS` | 0.26 | lids at least this far apart always count as open (stops reflections on glasses looking like closed eyes) |
| `REST_PITCH`, `REST_YAW` | 0°, 0° | your head angle when sitting normally, head turns are measured from here |
| `MAX_PITCH`, `MAX_YAW` | 30°, 35° | turning your head further than this from resting counts as turned away |
| `COVERED_CONTRAST` | 0.15 | an eye counts as covered when there's almost no contrast in it. Never tuned, it would need a hand over the eye |
| gaze (`k_h`, `center_x`...) | see [GEOMETRIC-GAZE-ESTIMATION.md](GEOMETRIC-GAZE-ESTIMATION.md) | where on the screen you're looking. Fitted to the nine-point targets: left/right, leaning and up/down |

### The calibration file

Shortened, the real one has every value, step and target:

```json
{
  "version": 1,
  "name": "Calibration 2026-10-18 14:05",
  "createdAt": "2026-10-18T14:05:30-04:00",
  "lastUsedAt": "2026-10-18T14:05:30-04:00",
  "screenWidth": 2560,
  "screenHeight": 1440,
  "gaze": {},
  "eyes": {
    "LIDS_OPEN_OPENNESS": 0.271,
    "REST_PITCH": -12.2,
    "REST_YAW": 2,
    "MAX_PITCH": 30,
    "MAX_YAW": 35
  },
  "eyeCheck": {
    "madeAt": "2026-10-18T14:05:30-04:00",
    "values": [
      {
        "name": "MAX_YAW",
        "label": "Head turn limit, left/right",
        "used": 35,
        "default": 35,
        "tuned": false,
        "measured": { "turnedFurthest": 18.2, "margin": 10, "needed": 28.2 },
        "explanation": "35°, the default. Turning your head further than this from resting counts as turned away, and the cursor stops. Following the dot to the corners your head moved up to 18.2° left/right. The limit has to be at least that plus 10° (28.2°), so the default already covers it."
      },
      {
        "name": "IRIS_HIDDEN_RATIO",
        "label": "Closed eye, iris check",
        "used": 0.65,
        "default": 0.65,
        "tuned": false,
        "measured": { "openHighest": 0.5, "closedLowest": 0.56, "gap": 0.06, "gapNeeded": 0.1, "openSamples": 412, "closedSamples": 590 },
        "explanation": "0.65, the default. An eye counts as closed when ... Your open eyes measured up to 0.50 and your closed eyes from 0.56, only 0.06 apart. At least 0.10 is needed ..."
      }
    ],
    "steps": [
      {
        "step": "LookAround",
        "instruction": "Keep both eyes open and follow the dot with your eyes.",
        "tries": 1,
        "passed": true,
        "frames": 204,
        "faceShare": 1,
        "left": {
          "irisRatio": { "p1": 0.29, "p5": 0.31, "median": 0.38, "p95": 0.47, "p99": 0.5 },
          "openness": { "p1": 0.29, "p5": 0.3, "median": 0.34, "p95": 0.37, "p99": 0.38 },
          "blink": {},
          "contrast": {},
          "widthPx": 58.5,
          "closedShare": 0,
          "coveredShare": 0,
          "offsetH": {},
          "offsetV": {},
          "foundShare": 1
        },
        "right": {},
        "pitch": { "p1": -21.4, "p5": -19.8, "median": -12.5, "p95": -4.1, "p99": -2.6 },
        "yaw": {},
        "roll": {},
        "captureFps": 30
      }
    ],
    "check": { "passed": true, "scores": { "left eye closed": 0.96, "right eye closed": 0.94, "both eyes open": 0.99 }, "needed": 0.9 },
    "warnings": [],
    "decision": "Used the values above."
  },
  "gazeTargets": [
    {
      "index": 0, "x": 0.08, "y": 0.08, "isValidation": false,
      "frames": 36, "usableFrames": 34,
      "leftH": { "p1": -0.31, "p5": -0.3, "median": -0.27, "p95": -0.24, "p99": -0.23 },
      "leftV": {}, "rightH": {}, "rightV": {},
      "pitch": {}, "yaw": {}
    }
  ]
}
```

(the `{}` are cut short here, in the file they're filled in like the others)

| Field | Meaning |
|-------|---------|
| `version` | file format, 1 for now. Bump it if a change means old files can't be read the same way |
| `name` | shown in the list |
| `createdAt` | when it was made |
| `lastUsedAt` | when it was last picked. The newest one is the one in use |
| `screenWidth`, `screenHeight` | primary screen size in WPF units when it was made (at 125% scaling a 1920x1080 screen is 1536x864) |
| `gaze` | the numbers for `GazeParams` in `tracker/gaze_model.py`, same names. Anything left out uses its default. `{}` means all defaults |
| `eyes` | the eye check's values, named like the constants in `eye_extractor.py` and `tracking_status.py` (see [EYE-CALIBRATION.md](EYE-CALIBRATION.md)). Anything left out uses its default |
| `eyeCheck.values` | one per value the eye check decides: what it's set to (`used`), the `default`, whether it was `tuned`, the numbers it came from (`measured`) and a plain `explanation` with those numbers. The result screen shows the explanations |
| `eyeCheck.steps` | what each step measured, from the frames after the 1.2 s reaction time of its last try: tries, pass or the `problem`, how often the face and each eye were found, and per eye the spread (1st, 5th, median, 95th, 99th percentile) of `irisRatio`, `openness` (lid gap / eye width), `blink`, `contrast` and the iris position (`offsetH`, `offsetV`), plus head `pitch`, `yaw`, `roll` and the camera's fps |
| `eyeCheck.check` | the check round: the share of frames the tracker got right per instruction, and if it passed (90% needed) |
| `eyeCheck.warnings` | the eyes are small in the image, the camera is slow, a step was left out... |
| `eyeCheck.decision` | what was done with it: used, or the closed eye values kept at their defaults |
| `gazeTargets` | the nine-point screen: per target where it was (0-1), whether it was the lean target (`moveHead`), where the face was in the camera image (`faceX`, `faceW`), the lid openness and vertical iris offset (`openness`, `offsetV`, both eyes averaged), how many frames, how many were usable (face found, both eyes open and not covered) and the spread of each eye's iris position (`leftH`, `leftV`, `rightH`, `rightV`) and of the head angle. This is what the gaze numbers get fitted to |
| `gazeFit` | how the left/right gaze numbers were fitted to `gazeTargets`: the numbers, how far off each target still is (`targets`, as a share of the screen width), the average and worst error, the error on targets it wasn't fitted on (`leaveOneOutError`), what the default numbers got (`defaultMeanError`), and an `explanation`. Up/down: `yFitted`, `yConst`, `yOpenness`, `yIris`, the errors (`yMeanError`, `yMaxError`, `yLeaveOneOutError`, `yDefaultMeanError`, as a share of the screen height), `yTargets` and `yExplanation`. `version`: which version of the fit made it. And the leaning correction: `headFitted`, `headXGain`, `headXRef`, how far the face moved while leaning (`leanRange`), the error while leaning without and with it (`leanErrorBefore`, `leanErrorAfter`) and `headExplanation` |

The `gaze` names are `k_h`, `k_v`, `left_h_baseline`, `left_v_baseline`, `right_h_baseline`, `right_v_baseline`, `center_x`, `center_y`, `gain_x`, `gain_y`. What they mean is in [GEOMETRIC-GAZE-ESTIMATION.md](GEOMETRIC-GAZE-ESTIMATION.md).

A file that isn't valid JSON is skipped (the others still load) and left where it is. In the tracker, unknown names, values that aren't numbers, or impossible ones (like `k_h` of 0) fall back to the defaults. Calibrations made before the report was added just don't have `eyeCheck` and `gazeTargets`.

### The recording

`recordings/<same name>.json` has every frame of the calibration, so the math can be improved and run again on it without calibrating again. Nothing reads it while mouSEE runs.

```json
{
  "version": 1,
  "eyeCheck": [ { "step": "CloseLeft", "try": 1, "passed": false, "problem": "Close your left eye all the way.", "frames": [] } ],
  "checkRound": [ { "trial": "OpenBoth", "frames": [] } ],
  "gaze": [ { "index": 0, "x": 0.08, "y": 0.08, "isValidation": false, "frames": [] } ]
}
```

- `eyeCheck`: every try of every step, failed ones too, including the reaction time at the start
- `checkRound`: each instruction of the check round
- `gaze`: each nine-point target, the frames while its ring filled

One frame:

```json
{ "ms": 1533, "faceFound": true, "yaw": 2.1, "pitch": -12.4, "roll": 0.8, "captureFps": 30, "faceX": 0.47, "faceY": 0.41, "faceW": 0.29,
  "left":  { "irisRatio": 0.38, "contrast": 0.52, "blink": 0.04, "openness": 0.34, "widthPx": 58.5, "closed": false, "covered": false, "offsetH": -0.02, "offsetV": 0.05 },
  "right": { "irisRatio": 0.36, "contrast": 0.5, "blink": 0.05, "openness": 0.33, "widthPx": 57.9, "closed": false, "covered": false, "offsetH": -0.03, "offsetV": 0.04 } }
```

`ms` is from the start of that step, try or target. An eye the tracker didn't find is left out. `faceX` / `faceY` are the middle of the face in the camera image and `faceW` its width, all 0-1 (mirrored, leaning to your right makes `faceX` bigger).

## How the tracker gets it

The app starts the tracker with `--calibration <file of the one in use>`. `load_calibration()` in `face_tracker.py` reads the `gaze` part into `GazeParams` (`params_from_dict()` in `gaze_model.py`) and passes it to `estimate_gaze()` for every frame, and sets the `eyes` values in `eye_extractor` / `tracking_status` (`apply_eye_thresholds()`). Without `--calibration`, or if the file can't be read, it uses the defaults and says so on stderr.

To try a calibration file with the tracker on its own:

```bash
make tracker ARGS="--calibration ../app/Calibration/Saved/2026-10-18_14-05-30.json"
```

(the path is relative to `tracker/`, that's where `make tracker` runs it)

## In code

| File | What's in it |
|------|--------------|
| `app/Calibration/CalibrationProfile.cs` | one saved calibration (`CalibrationProfile`), its gaze numbers (`GazeModel`) and eye check values (`EyeThresholds`) |
| `app/Calibration/CalibrationReport.cs` | the report (`EyeCheckReport`, `EyeValueReport`, `StepReport`, `Spread`...), the nine-point summary (`GazeTargetSummary`) and the recording (`CalibrationRecording`) |
| `app/Calibration/GazeFit.cs` | fitting the gaze numbers to the nine-point targets (`FitX`) and its report (`GazeFitReport`) |
| `app/Calibration/CalibrationStore.cs` | the saved calibrations: list, add, use, update, rename, delete, and their recordings (`SaveRecording`, `LoadRecording`) |
| `app/UI/CalibrationsWindow.xaml(.cs)` | the calibrations window, also the startup question |
| `app/App.xaml.cs` | asks on startup, `StartCalibration()` (eye check then nine-point screen, saves what both measured), restarts the tracker when the one in use changes |
| `app/Gaze/LookChoice.cs`, `app/UI/ChoicePrompt.xaml(.cs)` | answering yes / no by looking right / left |
| `app/AppPaths.cs` | where the folder is |
| `tracker/face_tracker.py` | `load_calibration()`, `apply_eye_thresholds()` |
| `tracker/gaze_model.py` | `params_from_dict()`, `load_params()` |

There's one `CalibrationStore` for the whole app:

```csharp
var calibrations = ((App)Application.Current).Calibrations;

CalibrationProfile? inUse = calibrations.Active;   // null = default values
foreach (var p in calibrations.All) { }              // newest used first

calibrations.Add(new CalibrationProfile { Gaze = new GazeModel { KH = 0.64 } }); // saves it and uses it
calibrations.Use(id);
calibrations.Rename(id, "evening light");
calibrations.Delete(id);

calibrations.Changed += () => { /* something was added, picked, renamed or deleted */ };
```

## How the gaze is fitted

When you press Accept on the nine-point screen, `App.StartGazeCalibration()` fits the left/right and up/down numbers to the targets and the leaning correction to the lean target (`GazeFit.Fit`, app/Calibration/GazeFit.cs) and saves them in `gaze`, with the report in `gazeFit`. The tracker restarts and the bubble uses them.

It's the model in `estimate_gaze()` (tracker/gaze_model.py), so what the fit measures is what the bubble does: each eye's angle is `asin((h - baseline) / k_h)`, the two are averaged, and `x = center_x + gain_x * tan(angle)`.

- **baselines**: where each iris sits when you look at the middle column. The eyes turn in a bit, so they're different for each eye (left -0.14, right +0.13 in the first recording)
- **k_h**: tried from 0.5 to 3, keeping the one that's best on targets it wasn't fitted on. Bigger is more linear. Real eyes have fit better with a big one than with the default 0.7
- **center_x, gain_x**: a straight line through `tan(angle)` against where the targets were

Only the targets with at least 10 frames where both eyes were open count, and not the accuracy check ones. It needs at least 3 targets in at least 2 columns, otherwise the gaze stays at the defaults.

The first real calibration: the default numbers put the left and right targets at -0.7 and 1.7 (way off the screen, the bubble was always further out than where you looked). Fitted, the targets land on average 3.5% of the screen width away (about 90 px on 2560), 10% at worst (bottom left, looking down changes how the eyes read), and about 4.6% on targets left out of the fit.

### Leaning left and right

The iris position only says where the eyes point in the head. Lean to the right and look at the same spot, and your eyes turn a little left to stay on it, so without a correction the bubble moves left. It was only right when you sat where you sat during calibration.

So after the nine targets the logo goes back to the middle for 8 seconds: keep looking at it and slowly lean left and right. The logo doesn't move but the head does, so whatever the bubble would do then is caused by leaning. The fit (`GazeFit.FitHeadX`) is the slope of "how far off the bubble would be" against "how far the middle of the face moved in the camera image" (from where it was during the nine targets):

```
x = center_x + gain_x * tan(angle) + head_x_gain * (face_x - head_x_ref)
```

`face_x` is the middle of the face box in the camera image (0-1), the tracker passes it to `estimate_gaze()` every frame. `head_x_ref` is where it was during the nine targets. It isn't fitted if the face moved less than 3% of the camera image width while leaning (not enough to tell), or if the result is bigger than 6 (not leaning, probably looked away). `gazeFit` says which, and how far off the bubble was while leaning without and with the correction.

Calibrations made before the lean target have no correction (`head_x_gain` 0), make a new one to get it.

### Up and down

The iris barely moves up and down in the tracker's numbers: the vertical offset is measured from the middle between the lids, and the lids follow the eye, so most of it cancels out (0.035 from the top row to the bottom row, against 0.8 left to right). The lids themselves move though: more open looking up, less looking down (0.34 at the top row, 0.31 in the middle, 0.27 at the bottom in the first calibration with this). So up/down is fitted from those (`GazeFit.FitY`):

```
y = y_const + y_openness * lid openness + y_iris * vertical iris offset
```

both averaged over the open eyes, a straight line through the nine targets. It needs at least 4 targets in at least 2 rows with lid openness measured, otherwise up/down stays at the defaults (`gazeFit.yExplanation` says why). Head pitch would fit sitting still slightly better but breaks when leaning (leaning changes it), so it's not used.

On the first accuracy test this took the up/down error from 24% of the screen height (the defaults) to 11% sitting normally and 4% leaning left. Leaning right got worse (6% to 14%, the eyes open unevenly then), which the lean work below is about. Staring at one point the bubble shakes about 2% of the height up and down, more than left/right (0.8%), since the lids are a small number made big.

### Fitting again

Each fit says which version of the fit made it (`gazeFit.version`, `GazeFit.Version`). When mouSEE starts, every calibration fitted by an older version gets fitted again from its recording (`App.FitOldCalibrations`), so improvements to the fit apply to calibrations that were already made. Bump the version when the fit changes. A calibration without a recording only gets fitted if it never was.

Calibrations made before the fit existed have the targets but empty gaze numbers, those get fitted the same way.

### What's left

- **looking down changes left/right**: the bottom row is squeezed together. A term for it (iris position times lid openness) took left/right from 4.4% to 3.4% on the accuracy test
- **leaning shrinks the mapping too**: not just shifts it. A lean step with dots on the left and right (not just the middle) would let the fit learn that, about 11% to 7% leaning right on the accuracy test
- **leaning closer or further**: the face gets bigger or smaller in the image, which changes how far the eyes turn for the same spot. The face width is recorded (`faceW`), it could be fitted with a lean forward and back step
- **turning the head**: head yaw is recorded too but not used, while leaning people also turn their head a bit, which the leaning correction partly takes in
- **jitter**: staring at one point, the bubble still moves from frame to frame, about 1% of the screen width left/right and 2% of the height up/down. That's the smoothing task (filter the bubble's position)

## Answering by looking

Every question in the calibration can be answered without hands: the startup question, the eye check's Begin and its result (continue or do it again), and the nine-point screen's Begin and its result (accept or try again).

- **look to the right** for yes, **to the left** for no, and hold it for 3 seconds. The bar on that side fills up while you hold it
- it starts counting only after your eyes have been in the middle once, so you can't answer a question before you've read it, and after an answer they have to come back to the middle before the next one
- a blink while holding doesn't start the count over
- it's the eyes that count, not head turns. Turning your head to the right while keeping your eyes on the screen turns your eyes to the left in their sockets, so head turns could read a yes as a no. Look with your eyes, past the edge of the screen is fine
- it uses the tracker's gaze direction (`eyes.gaze.h`, the same "left" / "right" as in the overlay's readout): more than 0.12 off center to start, it stays on that side down to 0.08 so it doesn't flicker
- while the steps or the nine-point targets run it's off, looking around is part of those

`LookChoice` (app/Gaze) is the logic, `ChoicePrompt` (app/UI) shows it and can be put in any window: `Choice.Ask("Begin", "Cancel")` and handle `Choice.Chosen`.

## Testing accuracy

The accuracy test checks how close the bubble lands to where you look with the calibration in use. Open it with **Test accuracy** in the calibrations window, or `make run ARGS="--accuracy-test"`. Begin and close by looking right or left, like the other screens.

- dots show up one by one: 15 sitting normally (5 columns by 3 rows, so points between the calibration's 3 columns are tested too), then 3 leaning left and 3 leaning right. About a minute
- each dot: 0.8 s for the eyes to get there, then 1.5 s where the tracker's bubble position is recorded
- the bubble isn't shown while it runs, otherwise you'd follow it instead of the dot
- at the end every dot is drawn as a ring, with a line to a filled dot where the bubble landed (median of its frames). Yellow sitting normally, blue leaning left, pink leaning right. Above it: how far off left/right it was per way of sitting, which side it leans to, and the error per column

Saved in `app/Calibration/Saved/tests/<date>.json` (one line), with the report and every frame of every dot (same frames as a recording, plus `estimateX` / `estimateY`, where the bubble was). That's what to look at to decide what to improve: whether more calibration points would help, whether the leaning correction holds up, whether one side needs its own fit. The numbers are in `app/Calibration/AccuracyTest.cs`.
