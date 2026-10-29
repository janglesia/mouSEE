# mouSEE: start here

New to the project? Read this page first. It explains what mouSEE does, how the pieces fit together, what all the calibration words mean, and where to find things. The other docs go deeper into one topic each, and this page links to them.

## What it does

mouSEE lets someone point at the screen with their eyes, using only a normal webcam. It's an accessibility tool for people who can't use their hands, so everything is meant to work hands-free: the calibration's instructions are read out loud, and questions are answered by looking left or right. Outside the calibration it stays quiet (the start-up re-center and the "calibrate now?" question are on screen only).

**Where it is today:** the camera tracks your face and eyes, a calibration learns how your eyes map to the screen, and a **bubble** (a small circle on the screen) shows where mouSEE thinks you're looking. **Moving the real mouse cursor and clicking aren't built yet.** That's the next big step, and it will use the bubble's position.

## The big picture

There are two programs that run together:

```
 webcam
   │
   ▼
 tracker/  (Python)                          app/  (C#, Windows)
 ┌───────────────────────────────┐          ┌──────────────────────────────────────────┐
 │ face_tracker.py               │          │ TrackerClient.cs   starts the tracker,    │
 │   reads the camera, finds the │  one     │                    reads its JSON lines   │
 │   face with MediaPipe         │  line of │                                          │
 │ eye_extractor.py              │  JSON    │ OverlayWindow      draws the bubble and   │
 │   where each iris is, how     │  per     │                    the status bar         │
 │   open each eye is, closed?   │  frame   │                                          │
 │ gaze_model.py                 │ ───────► │ calibration screens, saved calibrations, │
 │   turns that into a point on  │          │ settings                                  │
 │   the screen (0-1), using the │          │                                          │
 │   calibration's numbers       │          │                                          │
 │ tracking_status.py            │          │                                          │
 │   ok / eyes closed / lost     │          │                                          │
 └───────────────────────────────┘          └──────────────────────────────────────────┘
```

- **Why two languages:** Python has the good face-tracking library (MediaPipe), and C# makes Windows things easy (overlays, windows, later moving the cursor).
- **How they talk:** the app starts the tracker in the background. The tracker prints one line of JSON per camera frame (about 60 a second), and the app reads each line. The app passes options to the tracker on its command line, including which calibration file to use.

### From your eyes to the bubble

Every frame goes through these steps. Each one is a single place in the code:

| # | Step | Where |
|---|------|-------|
| 1 | Find the face and its 478 points, the head angle, blinks | MediaPipe, in `tracker/face_tracker.py` |
| 2 | Per eye: where the iris is inside the eye, how open the lids are, is it closed | `tracker/eye_extractor.py` |
| 3 | Turn that into a point on the screen with the calibration's numbers, correcting for leaning and tilting | `tracker/gaze_model.py` |
| 4 | Is tracking OK right now, or lost, and why | `tracker/tracking_status.py`, then `app/Tracker/TrackingMonitor.cs` decides when it counts |
| 5 | The **fine-tune**'s last correction, then the **re-center**'s shift | `App.Bubble()` in `app/App.xaml.cs`, with `app/Bubble/FineTune.cs` |
| 6 | The **head nudge**, if a nudge head mode is on | `app/Bubble/HeadModes/HeadNudge.cs` |
| 7 | Draw the bubble, and the where-to-sit hint if you moved too far | `app/UI/OverlayWindow.xaml.cs` |

## What the calibration words mean

The words that come up everywhere, in the order a user meets them:

| Word | What it is | When it happens |
|------|------------|-----------------|
| **Calibration** | Everything mouSEE learns about one person, saved as one file. You can have several and switch between them. | Made once, used every time |
| **Grid** | First part of a calibration: the mouSEE logo moves to 20 places (5 × 4) and you look at each one. That teaches it where on the screen your eyes point. | Start of a new calibration |
| **Head step** | End of the grid: 16 dots while leaning left and right and tilting your head up and down, so it learns what moving your head does. | After the grid's 20 places |
| **Gaze fit** | The math that turns the grid and head step into the numbers the tracker uses (`GazeFit`, in `app/Calibration/GazeGrid/`). | At the end of the grid, before its preview |
| **Eye check** | Last part of a calibration: you close both eyes a few times, so it learns when your eyes count as closed. Spoken instructions, under a minute. | After the grid's preview |
| **Re-center** | Look at a dot in the middle for 2 seconds. Corrects a small shift from sitting a bit differently today. | Every start, or Ctrl+Alt+R |
| **Accuracy test and fine-tune** | Dots all over the screen, sitting normally and leaning and tilting. It shows how far off the bubble is, then the calibration is fitted again with the dots and the bubble gets a final correction so it lands on them, kept if you want. For when the bubble is off on a later day. | When you want, Ctrl+Alt+F or from the calibrations window |
| **Head modes** | What moving your head does. **Eyes only** (default): nothing, the calibration's head correction undoes it. **Eyes + head nudge**: the same, plus the nudge. **Eyes + head nudge, no head correction**: the nudge without the head correction, for comparing. | Picked in the calibrations window |
| **Nudge** | Your eyes stop on a spot, the bubble turns yellow and holds, and turning or tilting your head pushes it the last bit. | In the nudge head modes |
| **Where-to-sit hints** | A yellow bar on the overlay: "Move a little to your left", "Tilt your head up a little"... because the calibration only knows the head near where it was. | While using mouSEE |
| **Answering by looking** | Yes or no without hands: look right for yes, left for no, for 3 seconds. | Every question in the calibration screens |

## What a user sees

1. **First start:** "Calibrate now?" Look right for yes.
2. The **grid**, then the **head step**. Then a preview: the bubble live with the new calibration, keep it or do the grid again. Then the **eye check**.
3. **Every start:** a quick **re-center**, then the overlay: a status bar in the bottom left, and the bubble where you look.
4. **Keys:** Ctrl+Alt+C calibrations, Ctrl+Alt+R re-center, Ctrl+Alt+F accuracy test and fine-tune, Ctrl+Alt+P pause, Ctrl+Alt+Q quit. The same letters without Ctrl+Alt work in the camera window.

## Where things are

### The code, by folder

The code is grouped by feature: each folder has the logic and the window for one thing, and a README.md that says what's in it. The test folders mirror these.

| Folder | What's in it |
|--------|--------------|
| `app/` | `App.xaml.cs` starts everything: the overlay, the keys, which calibration screen opens, and running a calibration. `AppPaths.cs` (where files go), `Stats.cs` (percentiles) |
| `app/Tracker/` | talking to the Python tracker (`TrackerClient`, `TrackerMessage`, `FrameSample`), and when tracking counts as lost (`TrackingMonitor`) |
| `app/Calibration/EyeCheck/` | the **eye check**: its window, its steps, working out the values, the check round, what it saves |
| `app/Calibration/GazeGrid/` | the **grid** and the **head step**: `GridWindow` (the screen), `CalibrationTarget` (the points and head poses), `GazeFit` (the math, in four files) |
| `app/Calibration/Saving/` | the **calibration file** (`CalibrationProfile`), the saved ones (`CalibrationStore`), the raw frames (`CalibrationRecording`), the calibrations window (`CalibrationListWindow`) |
| `app/Calibration/Accuracy/` | the **accuracy test and fine-tune**, and its window |
| `app/Bubble/` | what happens to the bubble after the calibration: **re-center**, **fine-tune**, the **where-to-sit hints** (`PositionGuide`), and `HeadModes/` (the **head modes** and the **nudge**) |
| `app/LookToAnswer/` | **answering by looking**: `LookChoice` (the logic), `ClosedEyesGate` ("done looking"), `ChoicePrompt` (the yes / no bars) |
| `app/UI/` | the **overlay** (`OverlayWindow`, which draws the bubble) and pieces the windows share: `Speaker` (reads out loud), `MiddleDot`, `SettingSlider`, `UiColors` |
| `app/Settings/` | every **setting** (`AppSettings.cs`), loading and saving them, turning them into tracker options |
| `app/Input/` | empty: moving the cursor and clicking go here |
| `tracker/` | the Python tracker, see [From your eyes to the bubble](#from-your-eyes-to-the-bubble) |

### Your own data (not in git)

| What | Where |
|------|-------|
| Your settings | `app/Settings/settings.json` |
| Your calibrations | `app/Calibration/Saved/<date>.json` |
| The raw frames of each calibration | `app/Calibration/Saved/recordings/` |
| Accuracy test results | `app/Calibration/Saved/tests/` |

### The tests

`make test` runs all of them. Python tests are in `tests/test_*.py`, one per tracker file. C# tests are in `tests/app.Tests/`, in the same folders as the code they test, one file per class, named after it (`Calibration/GazeGrid/GazeFitTests.cs` tests `GazeFit`). Fake frames and recordings a few tests share are in `tests/app.Tests/Fakes/`.

### The docs: which one to read

| Read this | When you want to |
|-----------|------------------|
| [README.md](../README.md) | install, run, see every file, fix common problems |
| [SETUP.md](SETUP.md) | install step by step |
| [COMMANDS.md](COMMANDS.md) | run things by hand, without make |
| [SETTINGS.md](SETTINGS.md) | see or add a setting, the keys |
| [LOOK-TO-ANSWER.md](LOOK-TO-ANSWER.md) | answering by looking, and the middle dot |
| **calibration/** | **making a calibration and what's in it** |
| [SAVED-CALIBRATIONS.md](calibration/SAVED-CALIBRATIONS.md) | the calibration file, the recording, which one is used, the calibrations window |
| [EYE-CALIBRATION.md](calibration/EYE-CALIBRATION.md) | the eye check: its steps, the math, how it's built |
| [GAZE-FIT.md](calibration/GAZE-FIT.md) | how the grid and the head step become the gaze numbers |
| [GEOMETRIC-GAZE-ESTIMATION.md](calibration/GEOMETRIC-GAZE-ESTIMATION.md) | the math and every number in `gaze_model.py` |
| [ACCURACY-TEST.md](calibration/ACCURACY-TEST.md) | the accuracy test |
| [GRID-POINTS.md](calibration/GRID-POINTS.md) | where exactly the grid's points are |
| [GRID-WINDOW-HISTORY.md](calibration/GRID-WINDOW-HISTORY.md) | the grid screen's changelog |
| **bubble/** | **using a calibration day to day** |
| [RE-CENTER.md](bubble/RE-CENTER.md) | re-centering |
| [FINE-TUNE.md](bubble/FINE-TUNE.md) | fine-tuning, and why the curves |
| [WHERE-TO-SIT.md](bubble/WHERE-TO-SIT.md) | the where-to-sit hints |
| [HEAD-MODES.md](bubble/HEAD-MODES.md) | the head modes and the nudge |

## Not built yet

- **Moving the cursor and clicking** (dwell or blink). The settings for it already exist (`clicking.*`), and `app/Input/` is the empty folder waiting for it.
- **Smoothing the bubble** so it shakes less.
- **A settings screen.** Settings are edited in `settings.json` for now, except the head and where-to-sit sliders in the calibrations window.
- **The database** in `persistence/database.py` is an early draft of where users and settings might be stored later. Nothing uses it yet.
