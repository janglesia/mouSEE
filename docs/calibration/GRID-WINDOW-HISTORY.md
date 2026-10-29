# Calibration Window: Developer Guide & Changelog

## Running the Calibration Window

To launch the calibration window preview, execute the following command from the project root:

```bash
dotnet run --project app -- --calibration-preview
```

> **DISCLAIMER:** If Windows blocks the build or execution, Smart App Control may need to be temporarily turned off in Windows Security.

---

## Calibration Workflow UI Changelog

### 10/7/2026 (2:14 PM)
* **Initial UI & Architecture Preview:** Added a nine-point calibration preview utilizing the *mouSEE* logo as the active target, complete with progress display and navigation.
* **Backend Hooks:** Exposed backend hooks for calibration start, target presentation and acceptance, completion, and cancellation, allowing the backend to report progress and status directly to the window.
* **Tracker Integration:** The tracker process is now managed via a shared `TrackerClient`, with the overlay consuming tracker events directly from it. 
* **Current Scope:** Calibration data capture, calculation, and persistence are not yet included—this update establishes the UI and integration framework. The project builds successfully.

### 10/10/2026 (11:05 PM)
* **Setup Screen & Validation:** Integrated live frame checking. The setup screen now validates whether a face and both eyes are detected, providing real-time feedback if tracking is interrupted.
* **Interactive Target Flow:** The *mouSEE* logo cycles through the 9 predefined screen positions. At each point, the logo shrinks onto the coordinate while a completion ring fills dynamically as the user maintains gaze.
* **Robust Error Handling:** Added fallback warnings if tracking is lost for $>0.5$ seconds (e.g., face leaving camera bounds, excessive head rotation, closed/covered eyes) or if the tracker stops responding altogether.
* **Results Screen & Metrics:** Implemented a summary screen displaying:
  * Number of targets captured with dual-eye tracking.
  * Tracking stability score.
  * Total duration of the calibration run.
  * *Accept* and *Try Again* interactive options.
* **Current Limitations & Next Steps:** 
  * The front end is fully operational, but full eye-data collection and mapping to screen positions are pending. Consequently, the accuracy tile displays "Not measured" and completed runs show "Unverified" instead of "Pass".
  * **Remaining components to build:**
    1. A collector storing eye-data during target capture phases.
    2. A mapping model translating data to screen coordinates.
    3. A validation step measuring errors across the 4 extra check targets (activated via `RunValidation`).
    4. A gaze predictor for the optional Test Tracking screen.
    5. Persistence logic for saving accepted calibration results (to be housed primarily within `app/Calibration`, pending database access design).

### 10/18/2026
* **Lean step:** after the 9 targets the logo goes back to the middle for 8 seconds and the user leans left and right while looking at it. It shows how much leaning shifts the gaze, see [GAZE-FIT.md](GAZE-FIT.md#moving-your-head-the-head-step). The frames while each ring fills are now collected and the gaze is fitted to them, so items 1, 2 and 5 of the list above are done.

### 10/19/2026
* **Head step:** the single lean dot is replaced by 8 dots: leaning left and right (dots on both sides) and tilting the head up and down (dots at the top and bottom). Each new position is read out and shown in big letters in the middle first (`HeadPose` in `app/Calibration/GazeGrid/CalibrationTarget.cs`). See [GAZE-FIT.md](GAZE-FIT.md#moving-your-head-the-head-step).
* **Fine-tune after accepting:** once the nine-point screen is accepted and fitted, the fine-tune opens: 20 more dots with the bubble hidden, then a live preview of the corrected bubble to keep or discard. See [FINE-TUNE.md](../bubble/FINE-TUNE.md).

### 10/19/2026 (later)
* **20-point grid:** the 9 points (3 by 3, 8% from the edges) are now 20 (5 columns by 4 rows, 7% from the edges). With 3 rows the fit couldn't see the curves in up/down, now it can fit them (`y_arc`, `y_openness_sq`). See [GAZE-FIT.md](GAZE-FIT.md#the-grid).
* **No fine-tune after accepting any more:** the grid fits the curves itself, the fine-tune is still in the calibrations window (Ctrl+Alt+F) for a later day.

### 10/20/2026
* **Grid first, then the eye check:** a new calibration starts with the grid. The eye check comes after it and takes its open eyes and resting head angle from the grid, so its own "sit still" and "follow the dot" steps are gone. See [EYE-CALIBRATION.md](EYE-CALIBRATION.md#the-order-grid-first).
* **Head step with 16 dots:** 4 per position, near the corners, instead of 2 (leaning only on the middle row, tilting only in the middle column). See [GAZE-FIT.md](GAZE-FIT.md#moving-your-head-the-head-step).
* **Accuracy check removed:** the 4 extra targets behind the "Run accuracy check" setting were never measured, the result screen always said "Not measured". The setting, the targets and the accuracy tile are gone, the accuracy test does this. See [GRID-POINTS.md](GRID-POINTS.md#2-validation-targets-removed).
* **Accuracy test and fine-tune in one:** the fine-tune isn't its own run any more, it's worked out from the accuracy test's dots (now 20 sitting normally, 5 by 4, plus the 12 leaning and tilting). Nothing is said out loud on the results. See [ACCURACY-TEST.md](ACCURACY-TEST.md).
