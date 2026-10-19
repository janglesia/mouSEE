using System.IO;
using System.Reflection;
using app.Settings;

namespace app.Tests;

// keeps settings in memory so the service can be tested without a file
file sealed class MemoryStore(AppSettings? saved = null) : ISettingsStore
{
    public AppSettings? Saved { get; private set; } = saved;
    public int Saves { get; private set; }
    public AppSettings? Load() => Saved;
    public void Save(AppSettings settings) { Saved = settings; Saves++; }
}

public class SettingsCatalogTests
{
    [Fact]
    public void Every_property_in_a_section_is_a_setting()
    {
        // a property without [Setting] wouldn't be clamped, loaded one at a time, or shown in a settings screen
        foreach (var section in typeof(AppSettings).GetProperties())
        {
            Assert.NotNull(section.GetCustomAttribute<SectionAttribute>());
            foreach (var prop in section.PropertyType.GetProperties())
                Assert.True(prop.GetCustomAttribute<SettingAttribute>() is not null,
                    $"{section.Name}.{prop.Name} needs a [Setting]");
        }
    }

    [Fact]
    public void Keys_are_unique_and_look_like_the_file()
    {
        var keys = SettingsCatalog.All.Select(s => s.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.Contains("clicking.dwellMs", keys);
        Assert.Contains("camera.resolution", keys);
    }

    [Fact]
    public void Numbers_have_a_range_and_the_default_is_in_it()
    {
        foreach (var s in SettingsCatalog.All.Where(s => s.Kind == SettingKind.Number))
        {
            Assert.False(double.IsNaN(s.Min) || double.IsNaN(s.Max) || double.IsNaN(s.Step), $"{s.Key} needs Min, Max and Step");
            double d = Convert.ToDouble(s.Default);
            Assert.InRange(d, s.Min, s.Max);
        }
    }

    [Fact]
    public void Every_setting_has_a_label_and_description()
    {
        foreach (var s in SettingsCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Label), s.Key);
            Assert.False(string.IsNullOrWhiteSpace(s.Description), s.Key);
        }
    }

    [Fact]
    public void Choices_list_every_enum_value_with_its_file_name()
    {
        var res = SettingsCatalog.Find("camera.resolution");
        Assert.Equal(SettingKind.Choice, res.Kind);
        Assert.Equal(["360p", "480p", "720p", "1080p"], res.Choices.Select(c => c.Key));
        Assert.Equal("1080p (1920 x 1080)", res.Choices[^1].Label);

        Assert.Equal(["dwell", "blink"], SettingsCatalog.Find("clicking.method").Choices.Select(c => c.Key));
    }

    [Fact]
    public void Every_setting_is_in_the_docs()
    {
        string docs = File.ReadAllText(FindRepoFile("docs/SETTINGS.md"));
        foreach (var s in SettingsCatalog.All)
            Assert.True(docs.Contains($"`{s.Key}`"), $"docs/SETTINGS.md doesn't mention `{s.Key}`");
    }

    private static string FindRepoFile(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string path = Path.Combine(dir.FullName, relative);
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException(relative);
    }
}

public class SettingInfoTests
{
    private static readonly AppSettings Defaults = new();

    [Fact]
    public void Get_and_with()
    {
        var dwell = SettingsCatalog.Find("clicking.dwellMs");
        Assert.Equal(1000, dwell.Get(Defaults));

        var changed = dwell.With(Defaults, 800);
        Assert.Equal(800, changed.Clicking.DwellMs);
        Assert.Equal(1000, Defaults.Clicking.DwellMs); // the original isn't touched
        Assert.Equal(Defaults.Camera, changed.Camera);
    }

    [Fact]
    public void Numbers_are_clamped_and_rounded()
    {
        var dwell = SettingsCatalog.Find("clicking.dwellMs");
        Assert.Equal(5000, dwell.With(Defaults, 99999).Clicking.DwellMs);
        Assert.Equal(200, dwell.With(Defaults, -5).Clicking.DwellMs);
        Assert.Equal(801, dwell.With(Defaults, 800.6).Clicking.DwellMs);
    }

    [Fact]
    public void Slider_doubles_work_for_int_settings()
    {
        // WPF sliders give doubles
        Assert.Equal(30, SettingsCatalog.Find("camera.fps").With(Defaults, 30.0).Camera.Fps);
    }

    [Fact]
    public void Bad_values_fall_back_to_the_default()
    {
        Assert.Equal(0.5, SettingsCatalog.Find("cursor.sensitivity").With(Defaults, double.NaN).Cursor.Sensitivity);
        Assert.Equal(1000, SettingsCatalog.Find("clicking.dwellMs").With(Defaults, "lots").Clicking.DwellMs);
        Assert.True(SettingsCatalog.Find("overlay.showStatusBar").With(Defaults, null).Overlay.ShowStatusBar);
    }

    [Fact]
    public void Choices_take_the_enum_or_its_file_name()
    {
        var res = SettingsCatalog.Find("camera.resolution");
        Assert.Equal(Resolution.P720, res.With(Defaults, Resolution.P720).Camera.Resolution);
        Assert.Equal(Resolution.P480, res.With(Defaults, "480p").Camera.Resolution);
        Assert.Equal(Resolution.P1080, res.With(Defaults, "4k").Camera.Resolution);
        Assert.Equal(Resolution.P1080, res.With(Defaults, (Resolution)42).Camera.Resolution);
    }
}

public class AppSettingsTests
{
    [Fact]
    public void Defaults_are_already_in_range()
    {
        Assert.Equal(new AppSettings(), new AppSettings().Normalized());
    }

    [Fact]
    public void Normalized_clamps_everything()
    {
        var s = new AppSettings
        {
            Camera = new() { Index = -1, Fps = 0 },
            Clicking = new() { DwellMs = 1 },
            Advanced = new() { RecoverFrames = 0, DetectionConfidence = 5 },
        }.Normalized();

        Assert.Equal(0, s.Camera.Index);
        Assert.Equal(15, s.Camera.Fps);
        Assert.Equal(200, s.Clicking.DwellMs);
        Assert.Equal(1, s.Advanced.RecoverFrames);
        Assert.Equal(0.9, s.Advanced.DetectionConfidence);
    }

    [Fact]
    public void Missing_section_gets_its_defaults()
    {
        var s = new AppSettings { Cursor = null! }.Normalized();
        Assert.Equal(new CursorSettings(), s.Cursor);
    }

    [Fact]
    public void Resolution_sizes_match_the_makefile()
    {
        Assert.Equal((640, 360), Resolution.P360.Size());
        Assert.Equal((640, 480), Resolution.P480.Size());
        Assert.Equal((1280, 720), Resolution.P720.Size());
        Assert.Equal((1920, 1080), Resolution.P1080.Size());
    }
}

public class SettingsServiceTests
{
    [Fact]
    public void First_run_uses_and_saves_the_defaults()
    {
        var store = new MemoryStore();
        var service = new SettingsService(store);
        Assert.Equal(new AppSettings(), service.Current);
        Assert.Equal(new AppSettings(), store.Saved);
    }

    [Fact]
    public void Loaded_settings_are_clamped_but_not_written_back()
    {
        // the file is only rewritten when something actually changes
        var store = new MemoryStore(new AppSettings { Clicking = new() { DwellMs = 50 } });
        var service = new SettingsService(store);
        Assert.Equal(200, service.Current.Clicking.DwellMs);
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public void Update_saves_and_tells_old_and_new()
    {
        var store = new MemoryStore(new AppSettings());
        var service = new SettingsService(store);
        AppSettings? seenOld = null, seenNew = null;
        service.Changed += (o, n) => (seenOld, seenNew) = (o, n);

        service.Update(s => s with { Clicking = s.Clicking with { DwellMs = 800 } });

        Assert.Equal(800, service.Current.Clicking.DwellMs);
        Assert.Equal(800, store.Saved!.Clicking.DwellMs);
        Assert.Equal(1000, seenOld!.Clicking.DwellMs);
        Assert.Same(service.Current, seenNew);
    }

    [Fact]
    public void Set_by_setting_info()
    {
        var service = new SettingsService(new MemoryStore(new AppSettings()));
        var paused = SettingsCatalog.Find("tracking.paused");

        service.Set(paused, true);

        Assert.True(service.Current.Tracking.Paused);
        Assert.Equal(true, service.Get(paused));
    }

    [Fact]
    public void Update_that_changes_nothing_does_nothing()
    {
        var store = new MemoryStore(new AppSettings());
        var service = new SettingsService(store);
        int changes = 0;
        service.Changed += (_, _) => changes++;

        service.Update(s => s with { Camera = s.Camera with { Index = 0 } });

        Assert.Equal(0, changes);
        Assert.Equal(0, store.Saves);
    }
}

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mouSEE-tests-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private AppSettings? LoadText(string json)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, json);
        return new JsonSettingsStore(FilePath).Load();
    }

    [Fact]
    public void Default_path_is_in_app_settings()
    {
        // tests run from tests/app.Tests/bin/..., so this finds the real project folder
        string path = new JsonSettingsStore().FilePath;
        Assert.EndsWith(Path.Combine("app", "Settings", "settings.json"), path);
    }

    [Fact]
    public void Missing_file_loads_as_null()
    {
        Assert.Null(new JsonSettingsStore(FilePath).Load());
    }

    [Fact]
    public void Save_then_load_round_trips()
    {
        var store = new JsonSettingsStore(FilePath);
        var settings = new AppSettings
        {
            Camera = new() { Index = 2, Resolution = Resolution.P720, Fps = 30, ShowPreview = true },
            Cursor = new() { Sensitivity = 0.25 },
            Clicking = new() { Method = ClickMethod.Blink, DwellMs = 750 },
            Tracking = new() { Paused = true },
        };
        store.Save(settings);
        Assert.Equal(settings, store.Load());
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public void Saved_file_uses_camel_case_and_readable_choices()
    {
        new JsonSettingsStore(FilePath).Save(new AppSettings());
        string json = File.ReadAllText(FilePath);
        Assert.Contains("\"dwellMs\": 1000", json);
        Assert.Contains("\"resolution\": \"1080p\"", json);
        Assert.Contains("\"method\": \"dwell\"", json);
    }

    [Fact]
    public void Hand_edited_file_with_missing_keys_comments_and_trailing_commas()
    {
        var loaded = LoadText("""
            {
              // only change the camera
              "camera": { "index": 1, "resolution": "720p", },
            }
            """)!;

        Assert.Equal(1, loaded.Camera.Index);
        Assert.Equal(Resolution.P720, loaded.Camera.Resolution);
        Assert.Equal(new CameraSettings().Fps, loaded.Camera.Fps);
        Assert.Equal(new ClickingSettings(), loaded.Clicking);
    }

    [Fact]
    public void One_bad_value_only_resets_that_one()
    {
        var loaded = LoadText("""{ "camera": { "index": 1, "resolution": "4k", "fps": "fast" } }""")!;
        Assert.Equal(1, loaded.Camera.Index);
        Assert.Equal(Resolution.P1080, loaded.Camera.Resolution);
        Assert.Equal(60, loaded.Camera.Fps);
    }

    [Fact]
    public void Out_of_range_values_are_clamped()
    {
        var loaded = LoadText("""{ "clicking": { "dwellMs": 50 } }""")!;
        Assert.Equal(200, loaded.Clicking.DwellMs);
    }

    [Fact]
    public void Null_section_and_unknown_keys_are_ignored()
    {
        var loaded = LoadText("""{ "cursor": null, "something": { "old": 1 } }""")!;
        Assert.Equal(new AppSettings(), loaded);
    }

    [Fact]
    public void Broken_file_loads_as_null_and_is_kept()
    {
        Assert.Null(LoadText("{ not json"));
        Assert.Equal("{ not json", File.ReadAllText(FilePath + ".invalid"));
    }
}

public class TrackerArgsTests
{
    [Fact]
    public void Settings_become_tracker_options()
    {
        var settings = new AppSettings
        {
            Camera = new() { Index = 1, Resolution = Resolution.P720, Fps = 30 },
            Advanced = new() { DetectionConfidence = 0.3 },
        };
        Assert.Equal(["--camera", "1", "--width", "1280", "--height", "720", "--fps", "30", "--min-confidence", "0.3"],
            TrackerArgs.Build(settings, []));
    }

    [Fact]
    public void Preview_only_when_turned_on()
    {
        Assert.DoesNotContain("--preview", TrackerArgs.Build(new AppSettings(), []));
        Assert.Contains("--preview", TrackerArgs.Build(new AppSettings { Camera = new() { ShowPreview = true } }, []));
    }

    [Fact]
    public void Command_line_comes_last_so_it_wins()
    {
        // argparse keeps the last value it sees
        var args = TrackerArgs.Build(new AppSettings { Camera = new() { Index = 1 } }, ["--camera", "2"]);
        Assert.Equal(["--camera", "2"], args[^2..]);
    }
}
