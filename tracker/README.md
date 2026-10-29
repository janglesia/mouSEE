# tracker/

The Python face tracker. The app starts it in the background, it reads the webcam and prints one line of JSON per frame. Run it on its own with `make tracker`. See [docs/OVERVIEW.md](../docs/OVERVIEW.md) for how it fits with the app, and [docs/COMMANDS.md](../docs/COMMANDS.md) for its options.

| | |
|---|---|
| `face_tracker.py` | reads the webcam, finds the face with MediaPipe, runs the others, prints the results. The comment at the top describes the output |
| `eye_extractor.py` | per eye: where the iris is, how open the lids are, closed or covered, and the glitch filter |
| `gaze_model.py` | turns the eye measurements into a point on the screen, with the calibration's numbers ([docs/calibration/GEOMETRIC-GAZE-ESTIMATION.md](../docs/calibration/GEOMETRIC-GAZE-ESTIMATION.md)) |
| `tracking_status.py` | per frame: ok, eyes closed, or lost (and why) |
| `requirements.txt` | the Python packages |
| `face_landmarker.task` | MediaPipe's face model, downloaded by `make setup` |
| `.venv/` | the Python environment, made by `make setup`, not in git |

Tests: `tests/test_*.py`, one per file.
