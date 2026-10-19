using System.IO;
using System.Text.Json.Nodes;
using app.Calibration;

namespace app.Tests;

public sealed class CalibrationStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mouSEE-tests-" + Guid.NewGuid().ToString("N"));
    private DateTimeOffset _now = new(2026, 10, 18, 14, 5, 0, TimeSpan.FromHours(-4));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private CalibrationStore Make() => new(_dir, () => _now);

    // a new calibration a minute after the last one
    private CalibrationProfile AddOne(CalibrationStore store, string name = "")
    {
        _now = _now.AddMinutes(1);
        return store.Add(new CalibrationProfile { Name = name, ScreenWidth = 1920, ScreenHeight = 1080 });
    }

    [Fact]
    public void Empty_folder_has_nothing_in_use()
    {
        var store = Make();
        Assert.Empty(store.All);
        Assert.Null(store.Active);
        Assert.Null(store.ActiveFile);
    }

    [Fact]
    public void Folder_defaults_to_app_calibration_saved()
    {
        Assert.EndsWith(Path.Combine("app", "Calibration", "Saved"), AppPaths.CalibrationsFolder);
    }

    [Fact]
    public void Add_saves_a_file_and_uses_it()
    {
        var store = Make();
        var saved = AddOne(store);

        Assert.Equal(saved, store.Active);
        Assert.True(File.Exists(store.ActiveFile));
        Assert.Equal("Calibration 2026-10-18 14:06", saved.Name);
        Assert.Equal(_now, saved.CreatedAt);
        Assert.Equal(_now, saved.LastUsedAt);
    }

    [Fact]
    public void Newest_is_in_use_and_it_survives_a_restart()
    {
        var store = Make();
        AddOne(store, "first");
        var second = AddOne(store, "second");

        Assert.Equal("second", store.Active!.Name);
        Assert.Equal("second", Make().Active!.Name); // as if mouSEE started again
        Assert.Equal(second.Id, Make().Active!.Id);
    }

    [Fact]
    public void Use_switches_and_is_remembered()
    {
        var store = Make();
        var first = AddOne(store, "first");
        AddOne(store, "second");

        _now = _now.AddMinutes(1);
        store.Use(first.Id);

        Assert.Equal("first", store.Active!.Name);
        Assert.Equal("first", Make().Active!.Name);
        Assert.Equal(["first", "second"], store.All.Select(p => p.Name));
    }

    [Fact]
    public void Deleting_the_one_in_use_goes_back_to_the_one_before()
    {
        var store = Make();
        AddOne(store, "first");
        var second = AddOne(store, "second");

        store.Delete(second.Id);

        Assert.Equal("first", store.Active!.Name);
        Assert.False(File.Exists(store.FileFor(second.Id)));
    }

    [Fact]
    public void Deleting_the_last_one_means_default_values()
    {
        var store = Make();
        var only = AddOne(store);
        store.Delete(only.Id);
        Assert.Null(store.Active);
        Assert.Null(store.ActiveFile);
    }

    [Fact]
    public void Rename_keeps_the_file_and_which_one_is_in_use()
    {
        var store = Make();
        var saved = AddOne(store);
        string file = store.ActiveFile!;

        store.Rename(saved.Id, "  evening light ");

        Assert.Equal("evening light", store.Active!.Name);
        Assert.Equal(file, store.ActiveFile);
        Assert.Equal("evening light", Make().Active!.Name);
    }

    [Fact]
    public void Blank_rename_is_ignored()
    {
        var store = Make();
        var saved = AddOne(store, "keep");
        store.Rename(saved.Id, "  ");
        Assert.Equal("keep", store.Active!.Name);
    }

    [Fact]
    public void Two_in_the_same_second_get_different_files()
    {
        var store = Make();
        var a = store.Add(new CalibrationProfile());
        var b = store.Add(new CalibrationProfile());
        Assert.NotEqual(a.Id, b.Id);
        Assert.Equal(2, Make().All.Count);
    }

    [Fact]
    public void Changed_fires_for_every_change()
    {
        var store = Make();
        int changes = 0;
        store.Changed += () => changes++;

        var saved = AddOne(store);
        _now = _now.AddMinutes(1);
        store.Use(saved.Id);
        store.Rename(saved.Id, "x");
        store.Delete(saved.Id);
        store.Delete("not-there");

        Assert.Equal(4, changes);
    }

    [Fact]
    public void Broken_file_is_skipped_and_the_rest_load()
    {
        var store = Make();
        AddOne(store, "good");
        File.WriteAllText(Path.Combine(_dir, "broken.json"), "{ not json");

        var reloaded = Make();
        Assert.Equal(["good"], reloaded.All.Select(p => p.Name));
        Assert.True(File.Exists(Path.Combine(_dir, "broken.json")));
    }

    [Fact]
    public void Gaze_numbers_use_the_tracker_names_and_missing_ones_are_left_out()
    {
        // tracker/gaze_model.py reads "gaze" straight into GazeParams, so the names have to match it
        var store = Make();
        store.Add(new CalibrationProfile { Gaze = new GazeModel { KH = 0.65, CenterY = 0.45 } });

        var gaze = JsonNode.Parse(File.ReadAllText(store.ActiveFile!))!["gaze"]!.AsObject();

        Assert.Equal(0.65, (double)gaze["k_h"]!);
        Assert.Equal(0.45, (double)gaze["center_y"]!);
        Assert.Equal(2, gaze.Count);
    }

    [Fact]
    public void File_round_trips()
    {
        var store = Make();
        var saved = store.Add(new CalibrationProfile
        {
            Name = "desk",
            ScreenWidth = 1536,
            ScreenHeight = 864,
            Gaze = new GazeModel { KH = 0.6, KV = 0.8, LeftHBaseline = 0.01, GainX = 1.9 },
        });
        Assert.Equal(saved, Make().Active);
    }
}
