using CodexHp.App.Infrastructure;
using CodexHp.Core.Positioning;
using CodexHp.Core.Settings;
using Xunit;

namespace CodexHp.App.Tests.Infrastructure;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly string localAppData = Path.Combine(
        Path.GetTempPath(),
        "CodexHp.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Load_creates_default_file_when_it_is_missing()
    {
        var store = new JsonSettingsStore(this.localAppData);

        var settings = store.Load();

        Assert.Equal(AppSettings.Default, settings);
        Assert.True(File.Exists(Path.Combine(this.localAppData, "CodexHp", "settings.json")));
    }

    [Fact]
    public void Load_migrates_schema_one_appearance_and_location_to_physical_defaults()
    {
        var settingsDirectory = this.SettingsDirectory();
        Directory.CreateDirectory(settingsDirectory);
        File.WriteAllText(Path.Combine(settingsDirectory, "settings.json"), """
        {
          "schemaVersion": 1,
          "startWithWindows": false,
          "colors": {
            "manaBar": "#010203"
          },
          "appearance": {
            "screenWidth": 400,
            "graphBarWidth": 0
          },
          "location": {
            "monitorId": "DISPLAY2",
            "x": 24.5,
            "y": 32.25
          }
        }
        """);
        var store = new JsonSettingsStore(this.localAppData);

        var settings = store.Load();

        Assert.False(settings.StartWithWindows);
        Assert.False(settings.ShowOnlyWhenChatGptRunning);
        Assert.Equal("#010203", settings.Colors.ManaBar.ToHex());
        Assert.Equal(AppSettings.Default.Colors.HpBar, settings.Colors.HpBar);
        Assert.Equal(AppSettings.CurrentSchemaVersion, settings.SchemaVersion);
        Assert.Equal(AppSettings.Default.Appearance.OverlayWidth, settings.Appearance.OverlayWidth);
        Assert.Equal(AppSettings.Default.Appearance.OverlayHeight, settings.Appearance.OverlayHeight);
        Assert.Equal(AppSettings.Default.Appearance.GraphBarWidth, settings.Appearance.GraphBarWidth);
        Assert.Equal(OverlayLocationSettings.Default, settings.Location);
        var repairedJson = File.ReadAllText(Path.Combine(settingsDirectory, "settings.json"));
        Assert.Contains($"\"schemaVersion\": {AppSettings.CurrentSchemaVersion}", repairedJson, StringComparison.Ordinal);
        Assert.Contains("showOnlyWhenChatGptRunning", repairedJson, StringComparison.Ordinal);
        Assert.Contains("hpBar", repairedJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_schema_two_preserves_integer_physical_position_and_valid_appearance()
    {
        var settingsDirectory = this.SettingsDirectory();
        Directory.CreateDirectory(settingsDirectory);
        File.WriteAllText(Path.Combine(settingsDirectory, "settings.json"), """
        {
          "schemaVersion": 2,
          "appearance": {
            "screenWidth": 400,
            "screenHeight": 80,
            "gaugePaneWidth": 120,
            "graphBarWidth": 4,
            "graphBarGap": 1,
            "statusStripeWidth": 6
          },
          "location": {
            "monitorId": "DISPLAY2",
            "x": 125,
            "y": 250
          }
        }
        """);
        var store = new JsonSettingsStore(this.localAppData);

        var settings = store.Load();

        Assert.Equal(400, settings.Appearance.OverlayWidth);
        Assert.Equal(80, settings.Appearance.OverlayHeight);
        Assert.Equal(new OverlayLocationSettings("DISPLAY2", 125, 250), settings.Location);
        var migratedJson = File.ReadAllText(Path.Combine(settingsDirectory, "settings.json"));
        Assert.Contains("\"overlayWidth\": 400", migratedJson, StringComparison.Ordinal);
        Assert.Contains("\"overlayHeight\": 80", migratedJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"screenWidth\"", migratedJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"screenHeight\"", migratedJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_schema_three_migrates_physical_values_to_logical_schema_four_on_the_matched_monitor()
    {
        var settingsDirectory = this.SettingsDirectory();
        Directory.CreateDirectory(settingsDirectory);
        File.WriteAllText(Path.Combine(settingsDirectory, "settings.json"), """
        {
          "schemaVersion": 3,
          "appearance": {
            "overlayWidth": 270,
            "overlayHeight": 68,
            "gaugePaneWidth": 100,
            "graphBarWidth": 2,
            "graphBarGap": 0,
            "statusStripeWidth": 4
          },
          "location": {
            "monitorId": "DISPLAY2",
            "x": 0,
            "y": 2078
          }
        }
        """);
        var monitor = new MonitorGeometry(
            "DISPLAY2",
            new PhysicalRect(0, 0, 3840, 2160),
            new PhysicalRect(0, 0, 3840, 2064),
            2,
            2,
            true,
            "MONITOR-STABLE-2");
        var taskbar = new PhysicalRect(0, 2064, 3840, 96);
        var store = new JsonSettingsStore(
            this.localAppData,
            monitors: () => [monitor],
            taskbarBounds: _ => taskbar);

        var settings = store.Load();

        Assert.Equal(4, settings.SchemaVersion);
        Assert.Equal(new AppearanceSettings(135, 34, 50, 1, 0, 2), settings.Appearance);
        Assert.Equal("MONITOR-STABLE-2", settings.Location.MonitorKey);
        Assert.Equal(OverlayPlacementTarget.Taskbar, settings.Location.Target);
        Assert.Equal(0, settings.Location.NormalizedX);
        Assert.Equal(0.5, settings.Location.NormalizedY);
        var migratedJson = File.ReadAllText(Path.Combine(settingsDirectory, "settings.json"));
        Assert.Contains("\"schemaVersion\": 4", migratedJson, StringComparison.Ordinal);
        Assert.Contains("\"monitorKey\": \"MONITOR-STABLE-2\"", migratedJson, StringComparison.Ordinal);
        Assert.Contains("\"target\": \"taskbar\"", migratedJson, StringComparison.Ordinal);
        Assert.Contains("\"normalizedY\": 0.5", migratedJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Save_replaces_settings_without_leaving_temporary_files()
    {
        var store = new JsonSettingsStore(this.localAppData);
        var expected = AppSettings.Default with
        {
            StartWithWindows = false,
            ShowOnlyWhenChatGptRunning = true,
            Appearance = AppSettings.Default.Appearance with { OverlayWidth = 420 },
        };

        store.Save(expected);
        var loaded = store.Load();

        Assert.Equal(expected, loaded);
        Assert.Empty(Directory.GetFiles(this.SettingsDirectory(), "settings.json.tmp-*"));
    }

    [Fact]
    public void Save_uses_usage_overlay_property_names()
    {
        var store = new JsonSettingsStore(this.localAppData);
        var settings = AppSettings.Default with
        {
            Appearance = AppSettings.Default.Appearance with
            {
                OverlayWidth = 420,
                OverlayHeight = 80,
            },
        };

        store.Save(settings);

        var json = File.ReadAllText(Path.Combine(this.SettingsDirectory(), "settings.json"));
        Assert.Contains("\"overlayWidth\": 420", json, StringComparison.Ordinal);
        Assert.Contains("\"overlayHeight\": 80", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"screenWidth\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"screenHeight\"", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_preserves_corrupt_file_and_recovers_defaults()
    {
        var settingsDirectory = this.SettingsDirectory();
        Directory.CreateDirectory(settingsDirectory);
        var settingsPath = Path.Combine(settingsDirectory, "settings.json");
        File.WriteAllText(settingsPath, "{ corrupt-json");
        var store = new JsonSettingsStore(
            this.localAppData,
            () => new DateTimeOffset(2026, 8, 15, 12, 34, 56, TimeSpan.Zero));

        var settings = store.Load();

        Assert.Equal(AppSettings.Default, settings);
        var preserved = Assert.Single(Directory.GetFiles(settingsDirectory, "settings.invalid-20260815-123456*.json"));
        Assert.Equal("{ corrupt-json", File.ReadAllText(preserved));
        Assert.Contains("schemaVersion", File.ReadAllText(settingsPath), StringComparison.Ordinal);
    }

    [Fact]
    public void Save_never_writes_authentication_or_usage_fields()
    {
        var store = new JsonSettingsStore(this.localAppData);

        store.Save(AppSettings.Default);

        var json = File.ReadAllText(Path.Combine(this.SettingsDirectory(), "settings.json"));
        Assert.DoesNotContain("access_token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("account_id", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("remainingPercent", json, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(this.localAppData))
        {
            Directory.Delete(this.localAppData, recursive: true);
        }
    }

    private string SettingsDirectory() => Path.Combine(this.localAppData, "CodexHp");
}
