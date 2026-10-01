# mouSEE

Control your mouse cursor with your eyes using a webcam. Built as an accessibility tool for people who can't easily use a regular mouse.

Right now it only does tracking: 60 times a second it finds 478 points on your face (including your irises), your head angle, and things like blinks, and prints them. Moving the actual cursor comes next.

## How it works

There are two programs:

- `tracker/` is a Python script that reads the webcam and tracks your face and eyes using Google's MediaPipe Face Landmarker.
- `app/` is a C# console app. It starts the tracker in the background and reads what it finds.

The tracker prints one line of JSON per frame and the app reads those lines. Python is used for the vision part because that's where the good libraries are. C# is used for the app because it's easier to do Windows things in (moving the cursor, settings window, etc.).

## Files

```
mouSEE/
  app/                 C# app
    mouSEE.csproj      .NET project file
    Program.cs         the app
    Calibration/, Gaze/, Input/, Settings/, UI/   empty for now, see below
  tracker/             Python face tracking
    face_tracker.py    face tracking script
    requirements.txt   Python packages
    face_landmarker.task   face landmark model (downloaded by make setup)
    .venv/             Python virtual environment (created by make setup, not in git)
  persistence/
    database.py        SQLite schema (users, settings, calibration)
  docs/                setup guide and full command reference
  tests/               empty for now
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
- `tests/`: automated tests

## Getting started

You need Windows 10/11, a webcam, Git, Python 3.12, the .NET 10 SDK and make (from MSYS2). Install steps are in [docs/SETUP.md](docs/SETUP.md). Then:

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

A camera window also opens, showing the landmarks on your face. Close it with `q` or stop everything with Ctrl+C.

MediaPipe prints some `INFO` / `WARNING` lines on startup. These are fine.

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

For install problems (`make` not found, wrong Python version, pip failing), see [docs/SETUP.md](docs/SETUP.md#troubleshooting).
