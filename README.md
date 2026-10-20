# mouSEE

Control your mouse cursor with your eyes using a webcam. Built as an accessibility tool for people who can't easily use a regular mouse.

**New here? Start with [docs/OVERVIEW.md](docs/OVERVIEW.md):** what mouSEE does, how the pieces fit together, what all the calibration words mean, and where everything is.

Where it is now: it tracks your face and eyes 60 times a second, a calibration learns how your eyes map to your screen, and a bubble shows where you're looking. Moving the actual cursor and clicking come next.

## How it works

There are two programs:

- `tracker/` is a Python script that reads the webcam and tracks your face and eyes using Google's MediaPipe Face Landmarker.
- `app/` is a C# Windows app (WPF). It starts the tracker in the background, reads what it finds, and shows it in an overlay. It also has the calibration screen.

The tracker prints one line of JSON per frame and the app reads those lines. Python is used for the vision part because that's where the good libraries are. C# is used for the app because it's easier to do Windows things in (moving the cursor, settings window, etc.).

## Prerequisites

| What | Version | Get it |
|------|---------|--------|
| Windows | 10 or 11 | |
| Webcam | any, 60 fps is best | |
| Git | any | [git-scm.com](https://git-scm.com/install/windows) |
| Python | 3.12 (not 3.13+), **64-bit x64/AMD64 installer, not ARM64** | [python.org](https://www.python.org/downloads/windows/), 3.12.10 "Windows installer (64-bit)" |
| .NET SDK | 10 | [dotnet.microsoft.com](https://dotnet.microsoft.com/download) |
| make | any, **from MSYS2** | MSYS2, `pacman -S make` |

OpenCV, MediaPipe and scikit-learn get installed by `make setup`, you don't need to install them yourself. Step by step install instructions are in [docs/SETUP.md](docs/SETUP.md).

**make has to come from MSYS2** (for now). The Makefile uses Unix commands like `touch` and `rm`, which Windows doesn't have. MSYS2 installs them next to `make` in `C:\msys64\usr\bin`, and that folder must be on your PATH (see [docs/SETUP.md](docs/SETUP.md) step 4). A `make` installed some other way (winget, Chocolatey, GnuWin32) comes without these commands, so `make setup` fails with "process_begin: CreateProcess(NULL, touch ...) failed".

**On ARM PCs (Snapdragon / Windows on ARM):** install the regular 64-bit (AMD64) Python, not the ARM64 one. OpenCV and MediaPipe don't have ready-made ARM64 Windows versions for Python 3.12, so `make setup` tries to build them from source and fails with "Unknown compiler(s)". The AMD64 Python runs fine on ARM through Windows' built-in emulation.

**Smart App Control** (Windows 11) blocks MediaPipe, because its `libmediapipe.dll` isn't digitally signed. If the tracker fails to start and Windows says an Application Control policy blocked it, turn Smart App Control off: Windows Security → App & browser control → Smart App Control settings → Off. Note that on most Windows 11 versions you can't turn it back on without resetting Windows.

## Files

Every folder has its own README.md saying what's in it and what belongs there. The code is grouped by feature: each feature's logic and its window sit in the same folder.

```
mouSEE/
  app/                         C# app (WPF)
    mouSEE.csproj              .NET project file, also lists the app's folders so every file can use every part
    App.xaml, App.xaml.cs      starts the app: the overlay, the keys, which calibration screen opens
    AppPaths.cs                where settings and calibrations are saved
    Stats.cs                   percentiles, and Spread (how a number was spread over a recording)
    Tracker/                   talking to the Python tracker
      TrackerClient.cs           starts the tracker and reads its messages
      TrackerMessage.cs          the shape of the tracker's JSON messages
      FrameSample.cs             one tracker frame as the app records it
      TrackingMonitor.cs         decides when tracking counts as lost, and what to tell the user
    Calibration/               everything that makes and keeps a calibration
      GazeGrid/                  part 1: the 20-point grid and the head step (GridWindow), and GazeFit, the math
      EyeCheck/                  part 2: closed eyes, from the grid's open ones (window, steps, analysis, check round, report)
      Saving/                    the calibration file, the saved ones, the recording, the calibrations window
      Accuracy/                  the accuracy test and fine-tune, and its window
      Saved/                     your calibrations (made by the app, not in git)
    Bubble/                    where the bubble ends up, after the calibration
      Recenter.cs, RecenterWindow  re-center: a dot in the middle, looked at for a moment
      FineTune.cs                fine-tune: from the accuracy test's dots, a last correction on the bubble
      PositionGuide.cs           the where-to-sit hints: which way to move or tilt, or to re-center
      HeadPosition.cs            where the head is (face position, size, tilt)
      HeadModes/                 what moving your head does: nothing, or pushing the bubble once it holds (the nudge)
    LookToAnswer/              yes / no without hands: look right for yes, left for no
      LookChoice.cs              the logic, held for 3 seconds
      ClosedEyesGate.cs          "done looking": eyes closed for 1.5 s before a yes / no counts
      ChoicePrompt.xaml(.cs)     the yes / no bars, put in any window
    UI/                        the overlay and pieces the windows share
      OverlayWindow.xaml(.cs)    the bubble, and the status bar in the bottom left corner
      MiddleDot.xaml(.cs)        the dot shown whenever you're asked to look at the middle
      Speaker.cs                 reads instructions out loud
      SettingSlider.cs           a slider for one setting, built from its description
      UiColors.cs                the calibration yellow and dark
    Settings/                  every user setting
      AppSettings.cs             every setting with its default, range and description
      SettingsCatalog.cs         the list of all settings, for a settings screen
      SettingsService.cs         the current settings, changing them, telling the app they changed
      SettingsStore.cs           saving them (settings.json)
      TrackerArgs.cs             turns the settings into tracker options
      SettingAttribute.cs        [Setting], [Section] and [Choice]
      settings.json              your settings (made by the app, not in git)
    Input/                     empty for now: moving the cursor and clicking go here
    assets/mouSEE-logo.png     logo, used as the calibration target
  tracker/                     Python face tracking
    face_tracker.py            reads the webcam, finds the face, sends results to the app
    eye_extractor.py           eye measurements, open/closed and winks, glitch filter, feature vectors
    gaze_model.py              turns the eye measurements into a point on the screen, with the calibration's numbers
    tracking_status.py         per frame: is tracking ok, eyes closed, or lost (and why)
    requirements.txt           Python packages
    face_landmarker.task       face landmark model (downloaded by make setup)
    .venv/                     Python virtual environment (created by make setup, not in git)
  persistence/
    database.py                SQLite schema draft (users, settings, calibration), not used yet
  docs/
    OVERVIEW.md                start here: how it all fits together, what the words mean, where things are
    SETUP.md                   installing everything, step by step
    COMMANDS.md                the commands make runs, for running things by hand
    SETTINGS.md                every setting, the keys, and how to add a setting
    LOOK-TO-ANSWER.md          answering by looking, and the middle dot
    calibration/               making a calibration and what's in it
      SAVED-CALIBRATIONS.md      the calibration file, the recording, which one is used
      EYE-CALIBRATION.md         the eye check: its steps, the math, how it's built
      GAZE-FIT.md                how the grid and head step become the gaze numbers
      GEOMETRIC-GAZE-ESTIMATION.md  the math and every number in gaze_model.py
      ACCURACY-TEST.md           the accuracy test
      GRID-POINTS.md             where exactly the grid's points are
      GRID-WINDOW-HISTORY.md     the grid screen's changelog
    bubble/                    using a calibration day to day
      RE-CENTER.md               re-centering
      FINE-TUNE.md               fine-tuning, and why the curves
      WHERE-TO-SIT.md            the where-to-sit hints
      HEAD-MODES.md              the head modes and the nudge
  tests/
    test_*.py                  Python tests, one per tracker file
    app.Tests/                 C# tests, in the same folders as app/ (Calibration/GazeGrid/GazeFitTests.cs...)
  Makefile                     shortcuts for setup, building and running (make setup, make run, ...)
```

**App.xaml.cs** starts the app. Normally it opens the overlay (and the re-center, or the "calibrate now?" question); with `--calibration-preview` it opens the grid screen instead. It also runs the calibration: the grid, then saving and fitting what it measured, then the eye check, which adds to it.

**Tracker/TrackerClient.cs** starts `tracker/face_tracker.py` (using `tracker/.venv` if it exists), passes it the camera settings and the app's command line options, and turns each line it prints into a `TrackerMessage`. It finds the `tracker` folder by searching upward from where the app runs. There's one `TrackerClient` for the whole app that all windows share.

**OverlayWindow** covers the screen without getting in the way: it draws the bubble where you look, and in the bottom left corner a coloured status bar saying whether tracking works (and what to do if not), and below it fps, head angle, irises, blinks and gaze. Clicks go through it to whatever is underneath, so it has no close button: press **Ctrl+Alt+Q** to close it, which also quits the app. **Ctrl+Alt+P** pauses and unpauses, **Ctrl+Alt+C** opens the saved calibrations, **Ctrl+Alt+R** re-centers, **Ctrl+Alt+F** fine-tunes. In the camera window, plain **q**, **p**, **c**, **r** and **f** do the same. A yellow bar under the status bar says when you've moved too far from where you sat during calibration (and which way to go), or when a re-center would help.

**Tracker/TrackingMonitor** takes the tracker's per-frame status and decides when it counts as lost: half a second of bad frames, so blinks don't. It also notices when the tracker stops sending anything, and knows when the user paused tracking. `CanMoveCursor` is what the cursor code should check later. The timings come from the settings.

**Settings** are in `app/Settings/`, see [Settings](#settings).

**GridWindow** (`app/Calibration/GazeGrid/`) is the grid screen: 20 points on the screen (5 columns by 4 rows), then the head step. The gaze is fitted to where your eyes were for each (`GazeFit`), see [docs/calibration/GAZE-FIT.md](docs/calibration/GAZE-FIT.md) and [docs/calibration/GRID-WINDOW-HISTORY.md](docs/calibration/GRID-WINDOW-HISTORY.md).

**The eye check** (`app/Calibration/EyeCheck/`) is the second part of a new calibration, after the grid. It reads out instructions (close both eyes, then the same with your head tilted up and down), checks you did each one and repeats it if not, and works out when your eyes count as closed, against the open eyes the grid recorded. It only needs your eyes and head, no hands. Its questions (begin, continue) are answered by looking right for yes or left for no for 3 seconds, and so are the startup question and the grid screen's. Then it checks the new value with the tracker using it. See [docs/calibration/EYE-CALIBRATION.md](docs/calibration/EYE-CALIBRATION.md).

**Saved calibrations** are kept in `app/Calibration/Saved/`, one file each, and the last one you used is the one in use. If there are none yet, mouSEE asks on startup if you want to calibrate, otherwise it uses default values. **CalibrationListWindow** (Ctrl+Alt+C, in `app/Calibration/Saving/`) lists them so you can switch, delete or make a new one, and picks the head mode. See [docs/calibration/SAVED-CALIBRATIONS.md](docs/calibration/SAVED-CALIBRATIONS.md).

**face_tracker.py** opens the webcam at 1920x1080 / 60 fps (or the closest thing the camera supports), runs MediaPipe Face Landmarker on every frame and prints the results. Coordinates are mirrored like a selfie camera, but "left eye" always means your actual left eye. With `--preview` it also opens a window showing what it sees. The comment at the top of the file describes the output format.

**eye_extractor.py** works out, for each eye, where the iris is within the eye, how open the eye is, whether it's closed (winks included) or covered by something like a hand (checked in the camera image: are the lids shut or the dark iris hidden, and is there any contrast at all), and combines the usable eyes into one gaze value. `EyeValidator` throws away frames where an eye suddenly jumps (MediaPipe glitches). It also builds the feature vectors that calibration will use.

**tracking_status.py** labels every frame `ok`, `eyes_closed` or `lost`, and for `lost` says why: no face, face cut off at the edge, head turned too far, eyes not found, too far from the camera, or eyes covered. It only looks at one frame at a time; deciding when a problem has lasted long enough to count is left to the app, so a blink doesn't count as losing tracking.

**face_landmarker.task** is a pretrained model from Google (about 4 MB). It finds 478 face points, including 10 for the irises, plus head rotation and 51 expression scores ("blendshapes") like `eyeBlinkLeft` or `jawOpen`. [Map of the point numbers](https://storage.googleapis.com/mediapipe-assets/documentation/mediapipe_face_landmark_fullsize.png).

**mouSEE.csproj** targets .NET 10 on Windows (WPF). If you have .NET 8 instead, change `net10.0-windows` to `net8.0-windows`.

**Makefile** has shortcuts so you don't have to remember the individual commands. See [Make commands](#make-commands).

`app/Input/` is a placeholder for moving the cursor and clicking (e.g. blink to click), which haven't been written yet. It has a README.md saying so.

**The bubble** (`app/Bubble/`): after the calibration puts the bubble roughly where you look, the re-center and fine-tune correct it for how you sit today, the head modes decide what moving your head does, and the where-to-sit hints tell you when you moved too far. See the [docs/bubble/](docs/bubble/) docs.

**Answering by looking** (`app/LookToAnswer/`): every question in the calibration screens is answered by looking right for yes or left for no. See [docs/LOOK-TO-ANSWER.md](docs/LOOK-TO-ANSWER.md).

## Settings

Settings are saved in `app/Settings/settings.json`, which the app creates with the defaults the first time it runs. It's not in git, everyone has their own. There's no settings screen yet, except the head mode, nudge and where-to-sit sliders in the calibrations window (Ctrl+Alt+C). For everything else, close the app, edit the file and start it again.

[docs/SETTINGS.md](docs/SETTINGS.md) lists every setting with its default and what it does, and explains how to use them in code, hook them up to a settings screen, and add new ones.

## Getting started

Once the prerequisites are installed:

```bash
git clone https://github.com/janglesia/mouSEE.git
cd mouSEE
make setup
```

The commands in this README are for Git Bash.

## Testing

### Tracker on its own

```bash
make tracker
```

You should see a window with a mesh of green dots over your face, red dots on your irises, outlines around your eyes with an `OPEN` / `CLOSED` / `COVERED` label, and your head angle and gaze in the top left. The bottom left shows the tracking status: green `OK`, orange `EYES_CLOSED`, or red `LOST` with the reason. Try turning your head, moving out of the picture, leaning far back or covering your eyes to see it change. The terminal will fill up with JSON lines. Press `q` in the window to quit.

MediaPipe prints some `INFO` / `WARNING` lines on startup. These are fine.

### Full app

```bash
make run
```

An overlay shows up in the bottom left corner of the screen. The coloured bar at the top tells you whether tracking is working, and if not, what's wrong and what to do:

| Colour | Means | Examples |
|--------|-------|----------|
| Green | Tracking | |
| Orange | Hold on | "Face found, hold still for a moment", "Eyes closed" |
| Red | Something's wrong | "Can't see your face, sit in front of the camera", "Head turned too far, face the screen", "Eyes are covered", "Tracker not responding" |
| Grey | Starting up, or paused | "Starting camera...", "Paused" |

Short problems like a blink or a one-frame glitch don't show up: something has to be wrong for half a second before the bar turns red, and after a problem it waits for a moment of good tracking before going green again. The rules are in `app/Tracker/TrackingMonitor.cs`.

Below the bar is the raw readout, something like:

```
messages  60.0 fps | capture  60.0 fps | landmark   11.8 ms | latency 25 ms
yaw    -3  pitch    -5  roll     1
iris L (0.455, 0.385)
iris R (0.550, 0.381)
blink L 0.18  R 0.13
gaze h +0.012  v -0.034  center
```

- `messages` is how many updates per second the app gets from the tracker (the actual tracking speed), `capture` is how fast the camera delivers frames, `landmark` is how long MediaPipe takes per frame, and `latency` is the time from the camera frame to the app.
- `yaw` / `pitch` / `roll` are your head angle in degrees (0 when facing the camera).
- `iris` is where each iris is in the image, from (0, 0) top left to (1, 1) bottom right.
- `blink` goes from 0 (open) to 1 (closed).
- `gaze` is where you're looking within your eyes, roughly -1 to +1 each way, plus a rough direction.

Everything should change as you move. A camera window also opens, showing the landmarks on your face. It only updates at 30 fps to save CPU, the overlay shows the actual tracking speed.

To quit, press **Ctrl+Alt+Q**, or `q` in the camera window. To pause, press **Ctrl+Alt+P**, or `p` in the camera window. Pausing stops the gaze bubble and turns the bar grey, press it again to carry on. **Ctrl+Alt+C** (or `c` in the camera window) opens the saved calibrations, **Ctrl+Alt+R** (or `r`) re-centers: look at the dot in the middle for a moment, and the bubble fits how you're sitting now. It also runs when mouSEE starts. **Ctrl+Alt+F** (or `f`) runs the accuracy test and fine-tune: dots all over the screen, then where the bubble landed and the bubble corrected to land on them, and you choose whether to keep it. The calibrations window also picks what your head does: Eyes only, or Eyes + head nudge, where the bubble holds when your eyes rest and your head pushes it the last bit (see [docs/bubble/HEAD-MODES.md](docs/bubble/HEAD-MODES.md)). The Ctrl+Alt keys work from any program, q, p, c, r and f only while the camera window is focused.

### Calibration screen

```bash
make run ARGS="--calibration-preview"
```

Opens only the grid calibration screen instead of the overlay, without the eye check after it. Keeping it at the end saves a calibration without eye check values. Close it with its close button.

## Unit tests

Run from the project folder:

```bash
make test
```

That runs both of these, which also work on their own:

```bash
tracker/.venv/Scripts/python -m unittest discover -s tests -v   # Python
dotnet test tests/app.Tests                                     # C#
```

Use the `.venv` Python, the packages the tests need aren't installed in your normal Python. The C# tests cover the settings, TrackingMonitor, the eye check, the gaze fit, saved calibrations, re-center, fine-tune, the accuracy test, the head modes and answering by looking. The Python tests cover eye extraction, the eye validator (glitch rejection, blinks, recovering after a real change), wink detection, feature vector generation, translation/scale invariance, and edge cases. `test_tracking_status` checks each ok / eyes_closed / lost case.

## Make commands

Run these from the project folder.

```bash
make            # list the commands
make setup      # create .venv, install packages, download model, build app
make build      # build the C# app
make run        # run the app with the camera window
make tracker    # run the tracker on its own with the camera window
make test       # run the Python and C# unit tests
make cameras    # list the cameras the tracker can open (for camera.index in app/Settings/settings.json)
make clean      # delete build output (app/bin, app/obj, tests/app.Tests/bin and obj, __pycache__)
make reset      # delete everything generated (build output, .venv, model) and set it all up again
```

### Resolution

Add `360`, `480`, `720` or `1080` after `make run` or `make tracker` to pick the camera resolution. Without one, it uses 1080.

```bash
make run 360        # 640x360
make run 480        # 640x480
make run 720        # 1280x720
make run 1080       # 1920x1080
make tracker 720
```

Which frame rate you get depends on the camera. Many webcams only do 30 fps at 480 but 60 fps at the others; the overlay shows what you're actually getting. Lower resolutions use less CPU, but the face and eyes are made of fewer pixels, so tracking gets less precise further from the camera.

### Other options

Pass any other tracker option with `ARGS`. This works for both `make run` and `make tracker`, and can be combined with a resolution:

```bash
make run 720 ARGS="--fps 30"
make tracker ARGS="--min-confidence 0.3"
```

### Switching cameras

By default the first camera Windows finds is used (camera `0`). Cameras are numbered from 0 in the order Windows lists them, so a laptop's built-in webcam is usually `0` and a USB webcam `1`. To see which numbers there are:

```bash
make cameras
```

That prints something like `{"type":"cameras","cameras":[{"index":0,"width":640,"height":480},{"index":1,...}]}`. It doesn't know the camera names, so if there's more than one, try them until the window shows the one you want.

To always use a different camera, set `camera.index` in `app/Settings/settings.json` (see [docs/SETTINGS.md](docs/SETTINGS.md)). To use one just for a single run, pass `--camera`:

```bash
make run ARGS="--camera 1"
make tracker ARGS="--camera 1"
```

If a number doesn't exist you'll get "Could not open camera N".

`make reset` is the fix for most "it worked yesterday" problems. Close the app first, otherwise Windows won't let it delete files that are in use.

Want to run things by hand, or use tracker options like `--min-confidence`? See [docs/COMMANDS.md](docs/COMMANDS.md).

## Troubleshooting

- **Could not open camera 0**: something else is using the webcam (Zoom, Teams, a browser tab). Close it, or if you have more than one camera, see [Switching cameras](#switching-cameras).
- **Model not found**: `face_landmarker.task` isn't in `tracker/`. Run `make setup`.
- **Can't find tracker/face_tracker.py**: the app looks for a `tracker` folder next to or above where it runs. This only happens if the app was copied somewhere outside the project. Use `make run`.
- **`make reset` fails to delete files**: the app or tracker is still running. Stop it and try again.
- **Low fps**: try a lower resolution, e.g. `make run 720`. Webcams also drop frame rate in dark rooms.
- **No face detected**: check lighting, stay within ~2 m, or try `--min-confidence 0.3`.
- **`make setup` fails with "Unknown compiler(s)" while installing opencv-python / numpy**: you have the ARM64 Python. Uninstall it, install the 64-bit (AMD64) Python 3.12, delete `tracker/.venv` and run `make setup` again. See [Prerequisites](#prerequisites).
- **`make setup` fails with "CreateProcess(NULL, touch ...) failed"** (or `rm` in `make clean` / `make reset`): your `make` isn't the MSYS2 one, or `C:\msys64\usr\bin` isn't on your PATH. See [Prerequisites](#prerequisites).
- **Tracker is blocked by an "Application Control policy"**: Smart App Control is blocking `libmediapipe.dll`. See [Prerequisites](#prerequisites) for how to turn it off.

For install problems (`make` not found, wrong Python version, pip failing), see [docs/SETUP.md](docs/SETUP.md#troubleshooting).
