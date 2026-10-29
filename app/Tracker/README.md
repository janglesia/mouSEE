# Tracker/

Talking to the Python tracker (`tracker/face_tracker.py`): starting it, reading its messages, and deciding when tracking counts as working.

| | |
|---|---|
| `TrackerClient.cs` | starts the tracker (with `tracker/.venv` if it's there), passes it the settings and options, turns each line it prints into a `TrackerMessage`. One for the whole app (`App.Tracker`) |
| `TrackerMessage.cs` | the shape of the tracker's JSON: faces, head angle, eyes, the gaze estimate, tracking status |
| `FrameSample.cs` | one frame as the app records it for calibrations, tests and the re-center |
| `TrackingMonitor.cs` | decides when tracking counts as lost (half a second of bad frames, so blinks don't), and what the status bar says |

Tests: `tests/app.Tests/Tracker/`.
