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