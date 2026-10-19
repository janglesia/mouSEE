# Saved calibrations

How mouSEE keeps calibrations, picks which one to use, and what's in the files.

The calibration itself (working out the numbers from where you looked) isn't built yet. Right now accepting a calibration saves one with no numbers in it, so it behaves like the default values. Everything else here works: saving, switching, deleting, and the tracker loading the one in use.

## How it works

- Every calibration is its own file in `app/Calibration/Saved/`. The folder is in `.gitignore`, everyone has their own.
- The one you used last is the one in use. A new calibration is used straight away, and picking an older one switches to it. It's still the one in use next time mouSEE starts.
- No calibrations: mouSEE uses the default values from `tracker/gaze_model.py` (`GazeParams`). They assume you're straight in front of the middle of the screen, so the cursor won't line up well.
- Deleting the one in use switches to the one you used before it, or back to the default values if it was the only one.

## On startup

If there are no saved calibrations, mouSEE asks if you want to calibrate:

- **Calibrate now** opens the calibration. Press Accept at the end and it's saved and used.
- **Use default values** closes the question. It asks again next time.
- Untick **Ask me to calibrate when mouSEE starts without a calibration** to stop the question. That's the `calibration.askWhenMissing` setting (see [SETTINGS.md](SETTINGS.md)), turn it back on there or tick the box again in the calibrations window.

Once there's at least one calibration it never asks, it just uses the last one.

## The calibrations window

Open it with **Ctrl+Alt+C** (from any program) or **c** in the camera window.

- the list shows every saved calibration, newest used first, with when it was made. The one in use says IN USE
- **Use this one** (or double-click) switches to the selected one
- **Delete** deletes the selected one, after asking
- **New calibration** opens the calibration, accepting it saves a new one and uses it
- if a calibration was made on a screen with a different size, it says so. It still works but won't line up

Switching restarts the tracker with the new calibration, which takes a few seconds (the status bar says "Starting camera...").

`make run ARGS="--calibration-preview"` opens the calibration on its own, without the overlay. Accepting there saves a calibration too.

## The files

`app/Calibration/Saved/2026-10-18_14-05-30.json`. The file name is when it was made, so renaming a calibration doesn't move the file.

```json
{
  "version": 1,
  "name": "Calibration 2026-10-18 14:05",
  "createdAt": "2026-10-18T14:05:30-04:00",
  "lastUsedAt": "2026-10-18T14:05:30-04:00",
  "screenWidth": 1536,
  "screenHeight": 864,
  "gaze": {
    "k_h": 0.64,
    "center_y": 0.47
  }
}
```

| Field | Meaning |
|-------|---------|
| `version` | file format, 1 for now. Bump it if a change means old files can't be read the same way |
| `name` | shown in the list |
| `createdAt` | when it was made |
| `lastUsedAt` | when it was last picked. The newest one is the one in use |
| `screenWidth`, `screenHeight` | primary screen size in WPF units when it was made (at 125% scaling a 1920x1080 screen is 1536x864) |
| `gaze` | the numbers for `GazeParams` in `tracker/gaze_model.py`, same names. Anything left out uses its default. `{}` means all defaults |

The `gaze` names are `k_h`, `k_v`, `left_h_baseline`, `left_v_baseline`, `right_h_baseline`, `right_v_baseline`, `center_x`, `center_y`, `gain_x`, `gain_y`. What they mean is in [GEOMETRIC-GAZE-ESTIMATION.md](GEOMETRIC-GAZE-ESTIMATION.md).

A file that isn't valid JSON is skipped (the others still load) and left where it is. In the tracker, unknown names, values that aren't numbers, or impossible ones (like `k_h` of 0) fall back to the defaults.

## How the tracker gets it

The app starts the tracker with `--calibration <file of the one in use>`. `face_tracker.py` reads the `gaze` part with `load_params()` from `gaze_model.py` and passes it to `estimate_gaze()` for every frame. Without `--calibration`, or if the file can't be read, it uses `DEFAULT_PARAMS` and says so on stderr.

To try a calibration file with the tracker on its own:

```bash
make tracker ARGS="--calibration ../app/Calibration/Saved/2026-10-18_14-05-30.json"
```

(the path is relative to `tracker/`, that's where `make tracker` runs it)

## In code

| File | What's in it |
|------|--------------|
| `app/Calibration/CalibrationProfile.cs` | one saved calibration (`CalibrationProfile`) and its gaze numbers (`GazeModel`) |
| `app/Calibration/CalibrationStore.cs` | the saved calibrations: list, add, use, rename, delete |
| `app/UI/CalibrationsWindow.xaml(.cs)` | the calibrations window, also the startup question |
| `app/App.xaml.cs` | asks on startup, `StartCalibration()`, restarts the tracker when the one in use changes |
| `app/AppPaths.cs` | where the folder is |
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

## What's left for the calibration itself

`App.StartCalibration()` saves the profile when you press Accept. That's where the real numbers go:

1. collect eye data while each target's ring fills (`TargetCaptureStarted` / `TargetAccepted` on `CalibrationWindow`), skipping frames where an eye is closed or covered
2. fit the `GazeParams` numbers to the 9 targets (`CalibrationCompleted`)
3. put them in `Gaze = new GazeModel { ... }` in `StartCalibration()`, instead of the empty one

The rest (saving, switching, loading them into the tracker) already works.
