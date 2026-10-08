# mouSEE

Control your mouse cursor with your eyes using a webcam. Built as an accessibility tool for people who can't easily use a regular mouse.

Right now it only does tracking: 60 times a second it finds 478 points on your face (including your irises), your head angle, and things like blinks, and prints them. Moving the actual cursor comes next.

## How it works

There are two programs:

- `tracker/` is a Python script that reads the webcam and tracks your face and eyes using Google's MediaPipe Face Landmarker.
- `app/` is a C# console app. It starts the tracker in the background and reads what it finds.

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

```
mouSEE/
  app/                 C# app
    mouSEE.csproj      .NET project file
    Program.cs         the app
    Calibration/, Gaze/, Input/, Settings/, UI/   empty for now, see below
  tracker/             Python face tracking
    face_tracker.py    face tracking script
    eye_extractor.py   eye/iris extraction and feature vector generation
    requirements.txt   Python packages
    face_landmarker.task   face landmark model (downloaded by make setup)
    .venv/             Python virtual environment (created by make setup, not in git)
  persistence/
    database.py        SQLite schema (users, settings, calibration)
  docs/                setup guide and full command reference
  tests/               automated tests
    test_eye_extractor.py  eye extractor and feature vector tests
  Makefile             shortcuts for setup, building and running (make setup, make run, ...)
```

**Program.cs** finds `../tracker`, starts `face_tracker.py` (using `tracker/.venv` if it exists), reads its output and shows a status line with fps, head angle, iris positions and blinks. Ctrl+C stops both.

**face_tracker.py** opens the webcam at 1920x1080 / 60 fps (or the closest thing the camera supports), runs MediaPipe Face Landmarker on every frame and prints the results. Coordinates are mirrored like a selfie camera, but "left eye" always means your actual left eye. With `--preview` it also opens a window showing what it sees. The comment at the top of the file describes the output format.

**face_landmarker.task** is a pretrained model from Google (about 4 MB). It finds 478 face points, including 10 for the irises, plus head rotation and 51 expression scores ("blendshapes") like `eyeBlinkLeft` or `jawOpen`. [Map of the point numbers](https://storage.googleapis.com/mediapipe-assets/documentation/mediapipe_face_landmark_fullsize.png).

**mouSEE.csproj** targets .NET 10. If you have .NET 8 instead, change `net10.0` to `net8.0`.

**Makefile** has shortcuts so you don't have to remember the individual commands. See [Make commands](#make-commands).

The empty folders are placeholders for features that haven't been written yet. Each has a `.gitkeep` file so git keeps the folder; delete it once real files go in.

- `app/Gaze/`: turning iris positions and head angle into a point on the screen
- `app/Calibration/`: looking at dots on screen so the program learns your eye range
- `app/Input/`: moving the cursor and clicking (e.g. blink to click)
- `app/UI/`: settings window, calibration screen, on/off toggle
- `app/Settings/`: user settings and saving them between runs (`persistence/database.py` has the database schema for this)

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

You should see a window with a mesh of green dots over your face, red dots on your irises, and your head angle in the top left. The terminal will fill up with JSON lines. Press `q` in the window to quit.

### Full app

```bash
make run
```

You should get something like:

```
Camera ready (1920x1080). Press Ctrl+C to stop.
 56.4 fps | yaw   -3 pitch   -5 roll    1 | iris L (0.455, 0.385) R (0.550, 0.381) | blink L 0.18 R 0.13
```

`yaw` / `pitch` / `roll` are your head angle in degrees (0 when facing the camera). `iris` is where each iris is in the image, from (0, 0) top left to (1, 1) bottom right. `blink` goes from 0 (open) to 1 (closed). Everything should change as you move. Ctrl+C to stop.

A camera window also opens, showing the landmarks on your face. Close it with `q` or stop everything with Ctrl+C. The window only updates at 30 fps to save CPU, the fps in the status line is the actual tracking speed.

MediaPipe prints some `INFO` / `WARNING` lines on startup. These are fine.

## Unit tests

Eye extractor and feature vector tests:
- Run from the project root with: `python -m unittest tests.test_eye_extractor`
- Tests cover eye extraction, feature vector generation, translation/scale invariance, and edge cases

## Make commands

Run these from the project folder.

```bash
make            # list the commands
make setup      # create .venv, install packages, download model, build app
make build      # build the C# app
make run        # run the app with the camera window
make tracker    # run the tracker on its own with the camera window
make clean      # delete build output (app/bin, app/obj, __pycache__)
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

Which frame rate you get depends on the camera. Many webcams only do 30 fps at 480 but 60 fps at the others; the status line shows what you're actually getting. Lower resolutions use less CPU, but the face and eyes are made of fewer pixels, so tracking gets less precise further from the camera.

### Other options

Pass any other tracker option with `ARGS`. This works for both `make run` and `make tracker`, and can be combined with a resolution:

```bash
make run 720 ARGS="--fps 30"
make tracker ARGS="--min-confidence 0.3"
```

### Switching cameras

By default the first camera Windows finds is used (camera `0`). To use a different one, pass `--camera` with its number:

```bash
make run ARGS="--camera 1"
make tracker ARGS="--camera 1"
```

Cameras are numbered from 0 in the order Windows lists them, so a laptop's built-in webcam is usually `0` and a USB webcam `1`. There's no list of which number is which, so try `1`, `2`, ... until the window shows the camera you want. If a number doesn't exist you'll get "Could not open camera N".

`make reset` is the fix for most "it worked yesterday" problems. Close the app first, otherwise Windows won't let it delete files that are in use.

Want to run things by hand, or use tracker options like `--min-confidence`? See [docs/COMMANDS.md](docs/COMMANDS.md).

## Troubleshooting

- **Could not open camera 0**: something else is using the webcam (Zoom, Teams, a browser tab). Close it, or if you have more than one camera, see [Switching cameras](#switching-cameras).
- **Model not found**: `face_landmarker.task` isn't in `tracker/`. Run `make setup`.
- **Can't find tracker**: you ran `dotnet run` from the wrong folder. Use `make run`, or `cd app` first.
- **`make reset` fails to delete files**: the app or tracker is still running. Stop it and try again.
- **Low fps**: try a lower resolution, e.g. `make run 720`. Webcams also drop frame rate in dark rooms.
- **No face detected**: check lighting, stay within ~2 m, or try `--min-confidence 0.3`.
- **`make setup` fails with "Unknown compiler(s)" while installing opencv-python / numpy**: you have the ARM64 Python. Uninstall it, install the 64-bit (AMD64) Python 3.12, delete `tracker/.venv` and run `make setup` again. See [Prerequisites](#prerequisites).
- **`make setup` fails with "CreateProcess(NULL, touch ...) failed"** (or `rm` in `make clean` / `make reset`): your `make` isn't the MSYS2 one, or `C:\msys64\usr\bin` isn't on your PATH. See [Prerequisites](#prerequisites).
- **Tracker is blocked by an "Application Control policy"**: Smart App Control is blocking `libmediapipe.dll`. See [Prerequisites](#prerequisites) for how to turn it off.

For install problems (`make` not found, wrong Python version, pip failing), see [docs/SETUP.md](docs/SETUP.md#troubleshooting).
