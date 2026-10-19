# mouSEE commands without make

The [Makefile](../Makefile) covers the usual tasks (see [Make commands](../README.md#make-commands)). This page lists the underlying commands, for when you want to run something by hand or use options make doesn't pass through.

Each group starts with the folder you need to be in (relative to the project folder).

## Setup (from `tracker/`)

```bash
cd tracker

# Create the Python environment (once)
py -3.12 -m venv .venv

# Install packages (once, or again after requirements.txt changes)
.venv/Scripts/python -m pip install -r requirements.txt

# Update packages to newer versions
.venv/Scripts/python -m pip install --upgrade -r requirements.txt

# See what's installed
.venv/Scripts/python -m pip list

# Download the model (once, if face_landmarker.task is missing)
curl -L -o face_landmarker.task https://storage.googleapis.com/mediapipe-models/face_landmarker/face_landmarker/float16/latest/face_landmarker.task
```

## Run the tracker on its own (from `tracker/`)

```bash
cd tracker

# Show the camera window with landmarks (q to quit)
.venv/Scripts/python face_tracker.py --preview

# No window, just print JSON lines to the terminal (Ctrl+C to stop)
.venv/Scripts/python face_tracker.py

# List all options
.venv/Scripts/python face_tracker.py --help

# Use a second camera
.venv/Scripts/python face_tracker.py --preview --camera 1

# Lower resolution (try this if fps is low)
.venv/Scripts/python face_tracker.py --preview --width 1280 --height 720

# 720p at 30 fps
.venv/Scripts/python face_tracker.py --preview --width 1280 --height 720 --fps 30

# Detect faces more easily (bad lighting) or more strictly (false detections)
.venv/Scripts/python face_tracker.py --preview --min-confidence 0.3
.venv/Scripts/python face_tracker.py --preview --min-confidence 0.8

# Track up to 2 faces
.venv/Scripts/python face_tracker.py --preview --num-faces 2

# List the cameras that open, as one JSON line (for camera.index in app/Settings/settings.json)
.venv/Scripts/python face_tracker.py --list-cameras

# Use a model file from somewhere else
.venv/Scripts/python face_tracker.py --preview --model C:/path/to/face_landmarker.task
```

Options can be combined in any order.

## Run the app (from `app/`)

`dotnet run` needs to be run where `mouSEE.csproj` is, so `cd app` first. The app finds the `tracker` folder by itself.

```bash
cd app

# Run with just the overlay (Ctrl+Alt+Q to quit, Ctrl+Alt+P to pause tracking)
dotnet run

# Run and also show the camera window
dotnet run -- --preview

# Use a second camera
dotnet run -- --preview --camera 1

# Start a new calibration straight away (eye check, then the nine-point screen)
dotnet run -- --calibrate

# Open the accuracy test (where the bubble lands for dots all over the screen)
dotnet run -- --accuracy-test

# Open only the nine-point calibration screen instead of the overlay
dotnet run -- --calibration-preview

# Build without running (to check for errors)
dotnet build

# Delete build output (bin/ and obj/)
dotnet clean
```

The `--` in `dotnet run -- --preview` separates options meant for `dotnet` from options meant for the app. The app passes all of its options on to the tracker, so every [tracker option](#tracker-options) works here too. The only options the app itself uses are `--calibrate`, `--calibration-preview` and `--accuracy-test`, it doesn't pass those on.

## Tracker options

```
--preview             show camera window
--camera N            which camera to use (default 0)
--width W --height H  resolution (default 1920x1080)
--fps N               frame rate (default 60)
--min-confidence X    detection threshold, 0-1 (default 0.5)
--num-faces N         max faces to track (default 1)
--model PATH          use a different model file
--list-cameras        print the cameras that open as JSON and exit
--calibration FILE    use a saved calibration (app/Calibration/Saved/*.json), see docs/SAVED-CALIBRATIONS.md
--record-eyes FILE    also write every frame's eye measurements to a CSV, see EYE-CALIBRATION.md
```

If the camera can't do what you ask for, it picks the closest mode it has.
