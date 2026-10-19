using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace app.Calibration;

/// <summary>
/// The saved calibrations, one .json each in app/Calibration/Saved/. The one used most recently is the one
/// in use (Active), so making a new one or picking one with Use switches to it, and it's still the one
/// next time mouSEE starts. One instance for the whole app (App.Calibrations), use from the UI thread.
/// </summary>
public sealed class CalibrationStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, // gaze values that weren't worked out
    };

    private readonly Func<DateTimeOffset> _now;
    private List<CalibrationProfile> _all = [];

    public string Folder { get; }

    /// <summary>Most recently used first.</summary>
    public IReadOnlyList<CalibrationProfile> All => _all;

    /// <summary>The one in use, null if there are none (the tracker then uses its default values).</summary>
    public CalibrationProfile? Active => _all.FirstOrDefault();

    /// <summary>The file of the one in use, for the tracker's --calibration.</summary>
    public string? ActiveFile => Active is { } a ? FileFor(a.Id) : null;

    /// <summary>Something was added, deleted, renamed or picked.</summary>
    public event Action? Changed;

    // folder and now are for tests
    public CalibrationStore(string? folder = null, Func<DateTimeOffset>? now = null)
    {
        Folder = folder ?? AppPaths.CalibrationsFolder;
        _now = now ?? (() => DateTimeOffset.Now);
        Reload();
    }

    /// <summary>Reads the folder again, e.g. after files were copied in by hand.</summary>
    public void Reload()
    {
        var list = new List<CalibrationProfile>();
        if (Directory.Exists(Folder))
        {
            foreach (var path in Directory.GetFiles(Folder, "*.json"))
            {
                try
                {
                    var profile = JsonSerializer.Deserialize<CalibrationProfile>(File.ReadAllText(path), Json);
                    if (profile is not null) list.Add(profile with { Id = Path.GetFileNameWithoutExtension(path) });
                }
                catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
                {
                    // skip it, the others still load. the file stays there to look at
                    Debug.WriteLine($"[calibration] can't read {path}: {ex.Message}");
                }
            }
        }
        _all = Sorted(list);
    }

    /// <summary>Saves a new calibration and switches to it. Fills in Id, the times, and a name if it has none.</summary>
    public CalibrationProfile Add(CalibrationProfile profile)
    {
        var now = _now();
        var saved = profile with
        {
            Id = NewId(now),
            Name = string.IsNullOrWhiteSpace(profile.Name) ? $"Calibration {now:yyyy-MM-dd HH:mm}" : profile.Name.Trim(),
            CreatedAt = now,
            LastUsedAt = now,
        };
        Write(saved);
        _all = Sorted([.. _all, saved]);
        Changed?.Invoke();
        return saved;
    }

    /// <summary>Switches to this one.</summary>
    public void Use(string id) => Replace(id, p => p with { LastUsedAt = _now() });

    public void Rename(string id, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        Replace(id, p => p with { Name = name.Trim() });
    }

    /// <summary>Deletes it. If it was the one in use, the one used before it takes over.</summary>
    public void Delete(string id)
    {
        if (_all.All(p => p.Id != id)) return;
        File.Delete(FileFor(id));
        _all = _all.Where(p => p.Id != id).ToList();
        Changed?.Invoke();
    }

    public string FileFor(string id) => Path.Combine(Folder, id + ".json");

    private void Replace(string id, Func<CalibrationProfile, CalibrationProfile> change)
    {
        var old = _all.FirstOrDefault(p => p.Id == id);
        if (old is null) return;
        var updated = change(old);
        if (updated == old) return;
        Write(updated);
        _all = Sorted(_all.Select(p => p.Id == id ? updated : p));
        Changed?.Invoke();
    }

    private void Write(CalibrationProfile profile)
    {
        Directory.CreateDirectory(Folder);
        // write next to it and swap, so a crash mid-write can't leave half a file
        string path = FileFor(profile.Id);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(profile, Json));
        File.Move(tmp, path, overwrite: true);
    }

    // the date it was made, so the files sort by age in Explorer too. -2, -3... if two are made in the same second
    private string NewId(DateTimeOffset now)
    {
        string id = now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
        string candidate = id;
        for (int i = 2; File.Exists(FileFor(candidate)) || _all.Any(p => p.Id == candidate); i++)
            candidate = $"{id}-{i}";
        return candidate;
    }

    private static List<CalibrationProfile> Sorted(IEnumerable<CalibrationProfile> profiles) =>
        profiles.OrderByDescending(p => p.LastUsedAt).ThenByDescending(p => p.CreatedAt).ToList();
}
