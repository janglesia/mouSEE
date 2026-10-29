using System.Diagnostics;
using System.IO;

namespace app.Settings;

/// <summary>
/// The app's settings, one instance for the whole app (App.Settings). Read Current, change them with Update or Set.
/// Both save the file and fire Changed. Use from the UI thread.
/// </summary>
public sealed class SettingsService
{
    private readonly ISettingsStore _store;

    public AppSettings Current { get; private set; }

    /// <summary>(old, new) after every change. Compare sections to see what changed, e.g. old.Camera != now.Camera.</summary>
    public event Action<AppSettings, AppSettings>? Changed;

    public SettingsService(ISettingsStore store)
    {
        _store = store;
        var saved = store.Load();
        Current = (saved ?? new AppSettings()).Normalized();
        // first run: write the defaults so there's a file to look at
        if (saved is null) TrySave(Current);
    }

    /// <summary>e.g. Update(s => s with { Clicking = s.Clicking with { DwellMs = 800 } })</summary>
    public void Update(Func<AppSettings, AppSettings> change)
    {
        var old = Current;
        var updated = change(old).Normalized();
        if (updated == old) return;

        Current = updated;
        TrySave(updated);
        Changed?.Invoke(old, updated);
    }

    /// <summary>For a settings screen: Set(SettingsCatalog.Find("clicking.dwellMs"), 800)</summary>
    public void Set(SettingInfo setting, object? value) => Update(s => setting.With(s, value));

    public object Get(SettingInfo setting) => setting.Get(Current);

    // a file that can't be written (open in another program, read-only) shouldn't crash the app,
    // the change still applies until it closes
    private void TrySave(AppSettings settings)
    {
        try
        {
            _store.Save(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"[settings] can't save: {ex.Message}");
        }
    }
}
