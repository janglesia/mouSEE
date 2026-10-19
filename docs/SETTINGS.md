# Settings

All of mouSEE's settings, where they're saved, and how to add new ones.

## Where they're saved

`app/Settings/settings.json`. The app writes it with the defaults the first time it runs. It's in `.gitignore`, so everyone has their own and nobody's camera or dwell time ends up in a commit. Delete it to go back to the defaults.

There's no settings screen yet, except the head mode, nudge and where-to-sit sliders in the calibrations window (Ctrl+Alt+C), which save as you move them. For everything else, close mouSEE, edit the file, and start it again (the file is only read at startup). `//` comments and trailing commas are fine.

What happens with bad values:

- out of range: clamped to the nearest allowed value (`"dwellMs": 50` becomes 200)
- wrong type or unknown choice (`"fps": "fast"`, `"resolution": "4k"`): that one setting uses its default, the rest still load
- missing: uses its default
- the file isn't JSON at all: everything uses its default, and the broken file is copied to `settings.json.invalid` so you don't lose it

Command line options win over the file for that run, e.g. `make run ARGS="--camera 1"` or `make run 720`. `make run` always passes `--preview`, so the camera window opens no matter what `camera.showPreview` says.

## Keys

| Keys | Where | Does |
|------|-------|------|
| Ctrl+Alt+Q | anywhere | quit |
| Ctrl+Alt+P | anywhere | pause / unpause (`tracking.paused`) |
| q | camera window | quit |
| p | camera window | pause / unpause |
| Ctrl+Alt+C | anywhere | the calibrations window, see [SAVED-CALIBRATIONS.md](calibration/SAVED-CALIBRATIONS.md) |
| c | camera window | the calibrations window |
| Ctrl+Alt+R | anywhere | re-center: look at a dot in the middle for a moment, see [RE-CENTER.md](bubble/RE-CENTER.md) |
| r | camera window | re-center |
| Ctrl+Alt+F | anywhere | fine-tune: last adjustments with 20 dots, see [FINE-TUNE.md](bubble/FINE-TUNE.md) |
| f | camera window | fine-tune |

The Ctrl+Alt ones work from any program. Plain q, p, c and r only work while the camera window is focused, otherwise typing a q anywhere would quit mouSEE.

## All settings

"When" says when a change takes effect if it's changed while the app runs (from code or a future settings screen). "Used" says whether anything reads it yet.

### Camera

| Setting | Default | Allowed | When | Used | What it does |
|---------|---------|---------|------|------|--------------|
| `camera.index` | `0` | 0 to 9 | restarts tracker | yes | Which camera. `make cameras` lists them. |
| `camera.resolution` | `"1080p"` | `"360p"`, `"480p"`, `"720p"`, `"1080p"` | restarts tracker | yes | The webcam's picture size, not your screen's: the screen is read from Windows and is never a setting. 640x360, 640x480, 1280x720 or 1920x1080, same as `make run 720` etc. Higher is more precise but uses more CPU. The camera picks its closest mode if it can't do it. |
| `camera.fps` | `60` | 15 to 120 | restarts tracker | yes | Frame rate to ask the camera for. Many webcams only do 30. |
| `camera.showPreview` | `false` | `true` / `false` | restarts tracker | yes | The camera window with the tracked points drawn on. |

### Cursor

| Setting | Default | Allowed | When | Used | What it does |
|---------|---------|---------|------|------|--------------|
| `cursor.sensitivity` | `0.5` | 0 to 1 | right away | not yet, the gaze filter will | Low keeps the cursor steady but it lags a bit, high follows faster but shakes more. |
| `cursor.showGazeBubble` | `true` | `true` / `false` | right away | yes | The circle where mouSEE thinks you're looking. |

### Clicking

| Setting | Default | Allowed | When | Used | What it does |
|---------|---------|---------|------|------|--------------|
| `clicking.method` | `"dwell"` | `"dwell"`, `"blink"` | right away | not yet, clicking (`app/Input`) will | Dwell clicks when you keep looking at the same spot, blink clicks when you blink on purpose. Same values as `click_method` in the database. |
| `clicking.dwellMs` | `1000` | 200 to 5000 ms | right away | not yet, dwell click will | How long you look at something before it's clicked. |
| `clicking.dwellRadiusPx` | `50` | 10 to 200 px | right away | not yet, dwell click will | How far the gaze can wander and still count as the same spot. |

### Tracking

| Setting | Default | Allowed | When | Used | What it does |
|---------|---------|---------|------|------|--------------|
| `tracking.paused` | `false` | `true` / `false` | right away | yes | The cursor stops following your eyes. The tracker keeps running so unpausing is instant. Saved, so it's still paused next time. Calibration ignores it. |

### Head

What moving your head does to the bubble, three ways to try against each other. Pick the mode in the calibrations window (Ctrl+Alt+C), see [HEAD-MODES.md](bubble/HEAD-MODES.md). The nudge numbers only matter in the `nudge` and `both` modes. They're also sliders in the calibrations window, under the mode.

| Setting | Default | Allowed | When | Used | What it does |
|---------|---------|---------|------|------|--------------|
| `head.mode` | `"cancel"` | `"cancel"` / `"both"` / `"nudge"` | right away (restarts the tracker when the head correction turns on or off) | yes | `cancel` = **Eyes only**: the head correction is on, moving your head doesn't move the bubble. `both` = **Eyes + head nudge**: the same, plus the bubble holds when your eyes rest and turning or tilting your head pushes it. `nudge` = **Eyes + head nudge, no head correction**: the nudge without the head correction, for comparing. The names are from before the modes were renamed. See [HEAD-MODES.md](bubble/HEAD-MODES.md). |
| `head.nudgeStrength` | `1.5` | 0.25 - 5, % of the screen per degree | right away | yes | How far the bubble moves for each degree you turn or tilt your head while nudging. |
| `head.nudgeDeadZone` | `1` | 0 - 5 degrees | right away | yes | Head movement smaller than this doesn't nudge, so the bubble doesn't drift while you hold still. |
| `head.nudgeSettleMs` | `600` | 100 - 3000 ms | right away | yes | How long the eyes stay on one spot before the bubble holds there and the head can nudge it. |
| `head.nudgeReleasePercent` | `20` | 5 - 50, % of the screen | right away | yes | How far the eyes move from both the held spot and the nudged bubble before the bubble follows them again. Raise it if the bubble lets go while you nudge. |

### Overlay

| Setting | Default | Allowed | When | Used | What it does |
|---------|---------|---------|------|------|--------------|
| `overlay.positionHintPercent` | `6` | 3 - 15, % of the camera image | right away | yes | How far you can move from where you sat during calibration (to the side, up or down) before the bar says which way to go back. Closer / further from the camera scales with it (1.8 / 1.5 times). Also in the calibrations window. |
| `overlay.tiltHintDegrees` | `8` | 4 - 20 degrees | right away | yes | How far your head can tilt up or down from the calibration before the bar says to tilt it back. Reading the bottom of the screen alone tilts it 3-4 degrees. Also in the calibrations window. |
| `overlay.showStatusBar` | `true` | `true` / `false` | right away | yes | The coloured bar in the bottom left saying whether tracking works. |
| `overlay.showDebugReadout` | `true` | `true` / `false` | right away | yes | fps, latency, head angle, irises, blinks and gaze under the status bar. |

### Calibration

| Setting | Default | Allowed | When | Used | What it does |
|---------|---------|---------|------|------|--------------|
| `calibration.targetDwellMs` | `1200` | 500 to 5000 ms | next calibration | yes | How long each target is looked at (the ring filling up). Longer gives calibration more samples. |
| `calibration.runAccuracyCheck` | `false` | `true` / `false` | next time the calibration window opens | yes | 4 extra targets after calibration to measure accuracy. Leave it off until something measures the error, otherwise you look at 4 targets for nothing. |
| `calibration.askWhenMissing` | `true` | `true` / `false` | next start | yes | When there are no saved calibrations, asks on startup if you want to make one. Without one mouSEE uses default values. The "ask me" box in the calibrations window sets it. See [SAVED-CALIBRATIONS.md](calibration/SAVED-CALIBRATIONS.md). |
| `calibration.recenterOnStart` | `true` | `true` / `false` | next start | yes | When mouSEE starts with a calibration, look at a dot in the middle for two seconds so the bubble fits how you're sitting today. Re-center any time with Ctrl+Alt+R or r in the camera window. See [RE-CENTER.md](bubble/RE-CENTER.md). |

### Advanced

Tuning, most people won't need to touch these.

| Setting | Default | Allowed | When | Used | What it does |
|---------|---------|---------|------|------|--------------|
| `advanced.lostAfterMs` | `500` | 100 to 5000 ms | right away | yes | How long tracking has to be bad before it counts as lost. Too short and every blink counts. |
| `advanced.recoverFrames` | `10` | 1 to 120 | right away | yes | Good frames in a row before tracking counts again after a problem (10 is about 170 ms at 60 fps). |
| `advanced.notRespondingAfterMs` | `1000` | 250 to 10000 ms | right away | yes | No frames for this long means the tracker is stuck. |
| `advanced.detectionConfidence` | `0.5` | 0.1 to 0.9 | restarts tracker | yes | Lower finds a face more easily in bad light, higher gives fewer false detections. The tracker's `--min-confidence`. |

### Example file

```jsonc
{
  "camera": { "index": 0, "resolution": "1080p", "fps": 60, "showPreview": false },
  "cursor": { "sensitivity": 0.5, "showGazeBubble": true },
  "clicking": { "method": "dwell", "dwellMs": 1000, "dwellRadiusPx": 50 },
  "tracking": { "paused": false },
  "head": { "mode": "cancel", "nudgeStrength": 1.5, "nudgeDeadZone": 1, "nudgeSettleMs": 600, "nudgeReleasePercent": 20 },
  "overlay": { "showStatusBar": true, "showDebugReadout": true, "positionHintPercent": 6, "tiltHintDegrees": 8 },
  "calibration": { "targetDwellMs": 1200, "runAccuracyCheck": false, "askWhenMissing": true },
  "advanced": { "lostAfterMs": 500, "recoverFrames": 10, "notRespondingAfterMs": 1000, "detectionConfidence": 0.5 }
}
```

You only need the ones you want to change, `{ "camera": { "index": 1 } }` is a valid file.

## Using settings in code

Everything is in `app/Settings/`:

| File | What's in it |
|------|--------------|
| `AppSettings.cs` | every setting, its default, range, label and description |
| `SettingsService.cs` | the current settings, changing them, the `Changed` event |
| `SettingsCatalog.cs` | the list of all settings for a settings screen |
| `SettingsStore.cs` | reading and writing `settings.json` |
| `SettingAttribute.cs` | the `[Setting]`, `[Section]` and `[Choice]` attributes |
| `TrackerArgs.cs` | turns the camera settings into `face_tracker.py` options |

There's one `SettingsService` for the whole app:

```csharp
var settings = ((App)Application.Current).Settings;

// read
int dwell = settings.Current.Clicking.DwellMs;

// change. saves the file and fires Changed
settings.Update(s => s with { Clicking = s.Clicking with { DwellMs = 800 } });

// react to changes. compare sections to see what changed
settings.Changed += (old, now) =>
{
    if (old.Clicking != now.Clicking) { /* use now.Clicking */ }
};
```

Values are always in range when you read them, `Update` clamps whatever you give it. Use it from the UI thread.

What already listens to `Changed`:

- `App.xaml.cs` restarts the tracker when its options change (camera, detection confidence, the calibration in use)
- `OverlayWindow` pauses, shows or hides the status bar, readout and bubble, and passes new timings to its `TrackingMonitor`
- `GridWindow` passes new timings to its `TrackingMonitor`

## Building a settings screen

`SettingsCatalog.All` has every setting, in the order they're declared, with what a screen needs to show it. You don't have to write one control per setting:

```csharp
foreach (var s in SettingsCatalog.All)
{
    // s.Section    "Clicking"           group heading
    // s.Label      "Dwell time"
    // s.Description                     tooltip / help text
    // s.Kind       Toggle, Number or Choice
    // s.Min, s.Max, s.Step, s.Unit      for a Number slider ("ms", "px"...)
    // s.Choices                         for a Choice dropdown, each has a Label to show and a Value
    // s.Default                         for a "reset" button
    object value = settings.Get(s);
}

// when the user changes a control
settings.Set(setting, newValue);
```

`Set` takes a double from a slider, a bool from a checkbox, or the `Value` of a dropdown choice, and clamps it. Put the Advanced section behind an "Advanced" expander.

`TrackerClient.ListCamerasAsync()` gives the cameras for a camera dropdown (same as `make cameras`). It takes a couple of seconds, and the camera the tracker is using might not show up while it's running.

## Adding a setting

1. Add a property to the right section in `AppSettings.cs` with a `[Setting]` on it. The initializer is the default:

   ```csharp
   [Setting("Click sound", "Plays a sound when something is clicked.")]
   public bool ClickSound { get; init; } = true;
   ```

   For a number give it a range, a step for the slider and a unit if it has one:

   ```csharp
   [Setting("Blink length", "How long a blink has to be to count as a click.", Min = 100, Max = 1000, Step = 50, Unit = "ms")]
   public int BlinkMs { get; init; } = 300;
   ```

   For a list of options make an enum, like `Resolution` or `ClickMethod`. `[JsonStringEnumMemberName]` is what's written in the file, `[Choice]` is what the dropdown shows.

2. Use it: `settings.Current.Clicking.BlinkMs`. If it should take effect right away, listen to `Changed`.

3. Add a row for it in this file. The tests fail if a setting is missing here.

That's it. Loading, saving, clamping, the settings screen list and the tests pick it up from the attribute.

A new section is a new record with its settings, and a property on `AppSettings` with `[Section("Name")]`.

If a setting is for the tracker, add the option to `face_tracker.py` and to `TrackerArgs.cs`. The app restarts the tracker whenever those options change.

Renaming a setting changes its key, so everyone's saved value for it goes back to the default.

## Later: the database

`persistence/database.py` has a `Settings` table with `dwell_time_ms` and `click_method`. Saving goes through `ISettingsStore` in `SettingsStore.cs`, so a SQLite version can replace the JSON file without changing anything else.
