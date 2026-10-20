# Saved calibrations

How mouSEE keeps calibrations, picks which one to use, and what's in the files.

A calibration has two parts. The **grid screen** comes first, for where on the screen you look: 20 points (5 columns by 4 rows), then the head step. The gaze numbers are fitted to them, see [How the gaze is fitted](GAZE-FIT.md). Then the **eye check**: when your eyes count as closed, using the grid's open eyes and head angle (see [EYE-CALIBRATION.md](EYE-CALIBRATION.md)).

## How it works

- Every calibration is its own file in `app/Calibration/Saved/`. The folder is in `.gitignore`, everyone has their own.
- The one you used last is the one in use. A new calibration is used straight away, and picking an older one switches to it. It's still the one in use next time mouSEE starts.
- No calibrations: mouSEE uses the default values: `GazeParams` in `tracker/gaze_model.py` for the gaze, which assume you're straight in front of the middle of the screen, and the constants in `eye_extractor.py` / `tracking_status.py` for closed, covered and head turned.
- Deleting the one in use switches to the one you used before it, or back to the default values if it was the only one.

## On startup

If there are no saved calibrations, mouSEE asks if you want to calibrate (on screen only, the voice is just for the calibration itself). You don't need your hands to answer: **look to the right** and hold it for 3 seconds for yes, **to the left** for no. A bar fills up on that side while you hold it. The buttons work as well. See [Answering by looking](../LOOK-TO-ANSWER.md).

- **Calibrate now** (or look right) starts a calibration: the grid screen, then the eye check. It's saved and used as soon as the grid is kept, the eye check adds to it.
- **Use default values** (or look left) closes the question. It asks again next time.
- Untick **Ask me to calibrate when mouSEE starts without a calibration** to stop the question. That's the `calibration.askWhenMissing` setting (see [SETTINGS.md](../SETTINGS.md)), turn it back on there or tick the box again in the calibrations window.

Once there's at least one calibration it never asks, it just uses the last one.

## The calibrations window

Open it with **Ctrl+Alt+C** (from any program) or **c** in the camera window.

- the list shows every saved calibration, newest used first, with when it was made. The one in use says IN USE
- **Use this one** (or double-click) switches to the selected one
- **Delete** deletes the selected one, after asking
- **Test and fine-tune** tests how accurate the calibration in use is, and fine-tunes it from the same dots if you want, see [Testing accuracy and fine-tuning](ACCURACY-TEST.md)
- **What your head does** picks one of the three head modes: Eyes only, Eyes + head nudge, or Eyes + head nudge without the head correction. See [HEAD-MODES.md](../bubble/HEAD-MODES.md)
- **New calibration** starts a new one (the grid screen, then the eye check), it's saved and used once the grid is kept
- if a calibration was made on a screen with a different size, it says so. It still works but won't line up

Switching restarts the tracker with the new calibration, which takes a few seconds (the status bar says "Starting camera...").

`make run ARGS="--calibrate"` starts a new calibration straight away. `make run ARGS="--calibration-preview"` opens only the grid screen, without the overlay and without the eye check after it. Keeping it there saves a calibration with no eye values.

## The files

Each calibration is two files:

- `app/Calibration/Saved/2026-10-18_14-05-30.json`: the calibration. What the tracker uses (`gaze`, `eyes`), plus a summary of everything that was measured and why each value is what it is. Readable, a few hundred lines
- `app/Calibration/Saved/recordings/2026-10-18_14-05-30.json`: every frame that was recorded, see [The recording](#the-recording). Big (around 1 MB), one line

The file name is when it was made, so renaming a calibration doesn't move the files.

### What "default" means

The defaults are the values built into the tracker (`eye_extractor.py`, `tracking_status.py`, `gaze_model.py`), picked by hand from short recordings of one person in one room. A calibration replaces the ones it can measure for you. The rest stay at the default, and the report says why.

| Value | Default | What it decides |
|-------|---------|-----------------|
| `LIDS_SHUT_OPENNESS` | 0.1 | lids closer together than this count as closed, whatever the iris check says. What the eye check tunes |
| `IRIS_HIDDEN_RATIO` | 0.65 | an eye counts as closed when its iris looks hidden (iris ratio above this). Catches winks. Only older eye checks tuned it |
| `LIDS_OPEN_OPENNESS` | 0.26 | lids at least this far apart always count as open (stops reflections on glasses looking like closed eyes). Only older eye checks tuned it |
| `REST_PITCH`, `REST_YAW` | 0°, 0° | your head angle when sitting normally, head turns are measured from here. From the grid |
| `MAX_PITCH`, `MAX_YAW` | 30°, 35° | turning your head further than this from resting counts as turned away. Older eye checks saved them, they're not tuned any more |
| `COVERED_CONTRAST` | 0.15 | an eye counts as covered when there's almost no contrast in it. Never tuned, it would need a hand over the eye |
| gaze (`k_h`, `center_x`...) | see [GEOMETRIC-GAZE-ESTIMATION.md](GEOMETRIC-GAZE-ESTIMATION.md) | where on the screen you're looking. Fitted to the grid targets: left/right, leaning and up/down |

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
    "LIDS_SHUT_OPENNESS": 0.15,
    "REST_PITCH": -13.6,
    "REST_YAW": 2.4
  },
  "eyeCheck": {
    "madeAt": "2026-10-18T14:05:30-04:00",
    "values": [
      {
        "name": "LIDS_SHUT_OPENNESS",
        "label": "Closed eyes, lid check",
        "used": 0.15,
        "default": 0.1,
        "tuned": true,
        "measured": { "openLowest": 0.248, "closedHighest": 0.047, "gap": 0.201, "gapNeeded": 0.05, "openSamples": 2900, "closedSamples": 520 },
        "explanation": "0.150 instead of the default 0.1. Lids closer together than this ... count as closed. Your open eyes measured at least 0.25 and your closed eyes at most 0.05, this is halfway between, but at most 0.15 ..."
      }
    ],
    "steps": [
      {
        "step": "CloseBoth",
        "instruction": "Close both eyes until you hear the sound.",
        "tries": 1,
        "passed": true,
        "frames": 85,
        "faceShare": 1,
        "left": {
          "irisRatio": { "p1": 0.51, "p5": 0.53, "median": 0.62, "p95": 0.76, "p99": 0.82 },
          "openness": { "p1": 0.035, "p5": 0.036, "median": 0.043, "p95": 0.045, "p99": 0.046 },
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
    "check": { "passed": true, "scores": { "both eyes closed": 0.96, "both eyes open": 0.99 }, "needed": 0.9 },
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
| `eyeCheck.decision` | what was done with it: used, or the closed eye value kept at its default |
| `gazeTargets` | the grid screen: per target where it was (0-1), whether it was the lean target (`moveHead`), the head step's position (`pose`: lean-left, lean-right, head-up, head-down), whether it's one of the fine-tune's dots (`fineTune`, numbered from 100), where the face was in the camera image (`faceX`, `faceY`, `faceW`), the lid openness and vertical iris offset (`openness`, `offsetV`, both eyes averaged), how many frames, how many were usable (face found, both eyes open and not covered) and the spread of each eye's iris position (`leftH`, `leftV`, `rightH`, `rightV`) and of the head angle. This is what the gaze numbers get fitted to |
| `fineTune` | the fine-tune's correction on the bubble, null without one: `x0`, `x1`, `x2`, `y0`, `y1`, `y2` and the curves `xuv`, `xuu`, `xvv`, `yuv`, `yuu`, `yvv` (see [Fine-tuning](../bubble/FINE-TUNE.md)) |
| `fineTuneReport` | what the fine-tune found: when (`madeAt`), how many dots had a bubble (`points`), whether the gaze numbers were fitted again with its dots (`refitted`) and how far off the bubble was on them before that (`errorXOldFit`, `errorYOldFit`), how far off it was without and with the correction (`errorXBefore`, `errorXAfter`, `errorYBefore`, `errorYAfter`, as a share of the screen, the "with" on dots it wasn't fitted on), which directions it corrects (`correctsX`, `correctsY`) and with which shape (`shapeX`, `shapeY`), an `explanation`, and every dot with where the bubble landed (`dots`) |
| `gazeFit` | how the left/right gaze numbers were fitted to `gazeTargets`: the numbers, the looking down squeeze (`xSqueeze`, `opennessRef`), how far off each target still is (`targets`, as a share of the screen width), the average and worst error, the error on targets it wasn't fitted on (`leaveOneOutError`), what the default numbers got (`defaultMeanError`), and an `explanation`. Up/down: `yFitted`, `yConst`, `yOpenness`, `yIris`, the errors (`yMeanError`, `yMaxError`, `yLeaveOneOutError`, `yDefaultMeanError`, as a share of the screen height), `yTargets` and `yExplanation`. `version`: which version of the fit made it. And the head step: `headFitted`, `headXGain`, `headXShrink`, `headYPitch`, `headYLean`, `headXRef`, `pitchRef`, the up/down error on its dots without and with (`headYErrorBefore`, `headYErrorAfter`), `headTargets`, how far the face moved while leaning (`leanRange`), the error while leaning without and with it (`leanErrorBefore`, `leanErrorAfter`) and `headExplanation` |

The `gaze` names are `k_h`, `k_v`, `left_h_baseline`, `left_v_baseline`, `right_h_baseline`, `right_v_baseline`, `center_x`, `center_y`, `gain_x`, `gain_y`. What they mean is in [GEOMETRIC-GAZE-ESTIMATION.md](GEOMETRIC-GAZE-ESTIMATION.md).

A file that isn't valid JSON is skipped (the others still load) and left where it is. In the tracker, unknown names, values that aren't numbers, or impossible ones (like `k_h` of 0) fall back to the defaults. Calibrations made before the report was added just don't have `eyeCheck` and `gazeTargets`.

### The recording

`recordings/<same name>.json` has every frame of the calibration, so the math can be improved and run again on it without calibrating again. It's only read when mouSEE starts, to fit calibrations from an older version of the fit again (see [GAZE-FIT.md](GAZE-FIT.md#fitting-again)).

```json
{
  "version": 2,
  "eyeCheck": [ { "step": "CloseBoth", "try": 1, "passed": false, "problem": "Keep both eyes closed until you hear the sound.", "frames": [] } ],
  "checkRound": [ { "trial": "OpenBoth", "frames": [] } ],
  "gaze": [ { "index": 0, "x": 0.07, "y": 0.07, "isValidation": false, "moveHead": false, "pose": null, "fineTune": false, "frames": [] } ]
}
```

- `version`: 2 since `offsetV` is measured from the eye corners, 1 before that, when it was from the middle of the lids. Version 1 recordings are fitted with up/down from the lids only, see [GAZE-FIT.md](GAZE-FIT.md#up-and-down)
- `eyeCheck`: every try of every step, failed ones too, including the reaction time at the start
- `checkRound`: each instruction of the check round
- `gaze`: each grid target, the frames while its ring filled, and the head step's. A kept fine-tune adds its dots here (`fineTune: true`), replacing an earlier one's

Older recordings have the eye check's old steps (`Rest`, `LookAround`, the winks) and check round (`CloseLeft`, `CloseRight`). The step names are plain text, they still load.

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
| `app/Calibration/Saving/CalibrationProfile.cs` | one saved calibration (`CalibrationProfile`), its gaze numbers (`GazeModel`) and eye check values (`EyeThresholds`) |
| `app/Calibration/Saving/CalibrationRecording.cs` | the recording: every raw frame of the eye check and the grid (`CalibrationRecording`) |
| `app/Calibration/EyeCheck/EyeCheckReport.cs` | the eye check's report (`EyeCheckReport`, `EyeValueReport`, `StepReport`...) and its raw frames |
| `app/Calibration/GazeGrid/GazeTargetSummary.cs` | what the eyes did at each grid target (`GazeTargetSummary`) and its raw frames |
| `app/Calibration/GazeGrid/GazeFit*.cs` | fitting the gaze numbers to the grid targets and the head step, see [GAZE-FIT.md](GAZE-FIT.md), and its report (`GazeFitReport.cs`) |
| `app/Stats.cs` | `Spread` (how a number was spread over a recording) and the percentiles |
| `app/Calibration/Saving/CalibrationStore.cs` | the saved calibrations: list, add, use, update, rename, delete, and their recordings (`SaveRecording`, `LoadRecording`) |
| `app/Calibration/Saving/CalibrationListWindow.xaml(.cs)` | the calibrations window, also the startup question |
| `app/App.xaml.cs` | asks on startup, `StartCalibration()` (the grid screen then the eye check, saves what both measured), `FitWithFineTune()` / `SaveFineTune()`, restarts the tracker when the one in use changes |
| `app/LookToAnswer/LookChoice.cs`, `app/LookToAnswer/ChoicePrompt.xaml(.cs)` | answering yes / no by looking right / left |
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
