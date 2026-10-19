# Settings/

Every user setting, saved in `settings.json` here (made by the app, not in git). See [docs/SETTINGS.md](../../docs/SETTINGS.md), which also says how to add one.

| | |
|---|---|
| `AppSettings.cs` | every setting, with its default, range and description |
| `SettingAttribute.cs` | `[Setting]`, `[Section]` and `[Choice]`, the descriptions a settings screen reads |
| `SettingsCatalog.cs` | the list of all settings, for building a settings screen |
| `SettingsService.cs` | the current settings, changing them, telling the app they changed. One for the whole app (`App.Settings`) |
| `SettingsStore.cs` | reading and writing `settings.json` |
| `TrackerArgs.cs` | turns the settings into the tracker's command line options |

Tests: `tests/app.Tests/Settings/`.
