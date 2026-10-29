using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace app.Settings;

/// <summary>Where settings are saved. JSON file for now, the SQLite Settings table could implement this later.</summary>
public interface ISettingsStore
{
    /// <summary>The saved settings, or null if there aren't any yet or the file can't be read.</summary>
    AppSettings? Load();
    void Save(AppSettings settings);
}

/// <summary>app/Settings/settings.json. Not in git, everyone has their own.</summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    public string FilePath { get; }

    public JsonSettingsStore(string? path = null) => FilePath = path ?? AppPaths.SettingsFile;

    public AppSettings? Load()
    {
        if (!File.Exists(FilePath)) return null;

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(FilePath), documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // keep a copy, the next save overwrites the file
            Debug.WriteLine($"[settings] can't read {FilePath}: {ex.Message}");
            try { File.Copy(FilePath, FilePath + ".invalid", overwrite: true); } catch (IOException) { }
            return null;
        }

        // read one setting at a time so a typo in one value only resets that one
        var settings = new AppSettings();
        foreach (var info in SettingsCatalog.All)
        {
            var parts = info.Key.Split('.');
            if (root?[parts[0]]?[parts[1]] is not { } node) continue;
            try
            {
                settings = info.With(settings, node.Deserialize(info.Type, SettingsCatalog.Json));
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
            {
                Debug.WriteLine($"[settings] {info.Key}: {ex.Message}, using {info.Default}");
            }
        }
        return settings;
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        // write next to it and swap, so a crash mid-write can't leave half a file
        string tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, SettingsCatalog.Json));
        File.Move(tmp, FilePath, overwrite: true);
    }
}
