using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodexHp.App.Application;
using CodexHp.Core.Settings;

namespace CodexHp.App.Infrastructure;

public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };

    private readonly Func<DateTimeOffset> clock;

    public JsonSettingsStore(string? localAppData = null, Func<DateTimeOffset>? clock = null)
    {
        var root = string.IsNullOrWhiteSpace(localAppData)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : localAppData;
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidOperationException("Local application data path is not available.");
        }

        this.SettingsDirectory = Path.Combine(root, "CodexHp");
        this.SettingsPath = Path.Combine(this.SettingsDirectory, "settings.json");
        this.clock = clock ?? (() => DateTimeOffset.Now);
    }

    public string SettingsDirectory { get; }

    public string SettingsPath { get; }

    public AppSettings Load()
    {
        Directory.CreateDirectory(this.SettingsDirectory);
        if (!File.Exists(this.SettingsPath))
        {
            this.Save(AppSettings.Default);
            return AppSettings.Default;
        }

        try
        {
            var json = File.ReadAllText(this.SettingsPath, Encoding.UTF8);
            var document = JsonSerializer.Deserialize<SettingsDocument>(json, SerializerOptions)
                ?? throw new JsonException("Settings document is empty.");
            var settings = Map(document);
            this.Save(settings);
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            this.PreserveInvalidSettings();
            this.Save(AppSettings.Default);
            return AppSettings.Default;
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(this.SettingsDirectory);
        var validated = SettingsValidator.Validate(settings).Settings;
        var document = SettingsDocument.From(validated);
        var json = JsonSerializer.Serialize(document, SerializerOptions) + Environment.NewLine;
        var temporaryPath = this.SettingsPath + ".tmp-" + Guid.NewGuid().ToString("N");

        try
        {
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, this.SettingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static AppSettings Map(SettingsDocument document)
    {
        var defaults = AppSettings.Default;
        var usesPhysicalPixels = document.SchemaVersion is >= 2 and <= AppSettings.CurrentSchemaVersion;
        var colors = new ColorSettings(
            ParseColor(document.Colors?.ManaBar, defaults.Colors.ManaBar),
            ParseColor(document.Colors?.HpBar, defaults.Colors.HpBar),
            ParseColor(document.Colors?.RefreshGauge, defaults.Colors.RefreshGauge),
            ParseColor(document.Colors?.ServiceIssue, defaults.Colors.ServiceIssue),
            ParseColor(document.Colors?.ServiceUnknown, defaults.Colors.ServiceUnknown),
            ParseColor(document.Colors?.TokenLow, defaults.Colors.TokenLow),
            ParseColor(document.Colors?.TokenHigh, defaults.Colors.TokenHigh));
        var appearance = usesPhysicalPixels
            ? new AppearanceSettings(
                document.Appearance?.OverlayWidth
                    ?? document.Appearance?.LegacyV2Width
                    ?? defaults.Appearance.OverlayWidth,
                document.Appearance?.OverlayHeight
                    ?? document.Appearance?.LegacyV2Height
                    ?? defaults.Appearance.OverlayHeight,
                document.Appearance?.GaugePaneWidth ?? defaults.Appearance.GaugePaneWidth,
                document.Appearance?.GraphBarWidth ?? defaults.Appearance.GraphBarWidth,
                document.Appearance?.GraphBarGap ?? defaults.Appearance.GraphBarGap,
                document.Appearance?.StatusStripeWidth ?? defaults.Appearance.StatusStripeWidth)
            : defaults.Appearance;
        var location = usesPhysicalPixels
            ? new OverlayLocationSettings(
                string.IsNullOrWhiteSpace(document.Location?.MonitorId) ? null : document.Location.MonitorId,
                ParsePhysicalCoordinate(document.Location?.X, defaults.Location.X),
                ParsePhysicalCoordinate(document.Location?.Y, defaults.Location.Y))
            : defaults.Location;
        var settings = new AppSettings(
            SchemaVersion: AppSettings.CurrentSchemaVersion,
            StartWithWindows: document.StartWithWindows ?? defaults.StartWithWindows,
            ShowOnlyWhenChatGptRunning: document.ShowOnlyWhenChatGptRunning ?? defaults.ShowOnlyWhenChatGptRunning,
            Colors: colors,
            Appearance: appearance,
            Location: location);
        return SettingsValidator.Validate(settings).Settings;
    }

    private static ColorValue ParseColor(string? value, ColorValue defaultValue) =>
        ColorValue.TryParse(value, out var parsed) ? parsed : defaultValue;

    private static int ParsePhysicalCoordinate(double? value, int defaultValue)
    {
        if (value is not double coordinate
            || !double.IsFinite(coordinate)
            || coordinate < int.MinValue
            || coordinate > int.MaxValue
            || coordinate != Math.Truncate(coordinate))
        {
            return defaultValue;
        }

        return (int)coordinate;
    }

    private void PreserveInvalidSettings()
    {
        var timestamp = this.clock().ToString("yyyyMMdd-HHmmssfff");
        var preservedPath = Path.Combine(this.SettingsDirectory, $"settings.invalid-{timestamp}.json");
        if (File.Exists(preservedPath))
        {
            preservedPath = Path.Combine(
                this.SettingsDirectory,
                $"settings.invalid-{timestamp}-{Guid.NewGuid():N}.json");
        }

        File.Move(this.SettingsPath, preservedPath);
    }

    private sealed class SettingsDocument
    {
        public int? SchemaVersion { get; set; }

        public bool? StartWithWindows { get; set; }

        public bool? ShowOnlyWhenChatGptRunning { get; set; }

        public ColorSettingsDocument? Colors { get; set; }

        public AppearanceSettingsDocument? Appearance { get; set; }

        public OverlayLocationDocument? Location { get; set; }

        public static SettingsDocument From(AppSettings settings) => new()
        {
            SchemaVersion = settings.SchemaVersion,
            StartWithWindows = settings.StartWithWindows,
            ShowOnlyWhenChatGptRunning = settings.ShowOnlyWhenChatGptRunning,
            Colors = new ColorSettingsDocument
            {
                ManaBar = settings.Colors.ManaBar.ToHex(),
                HpBar = settings.Colors.HpBar.ToHex(),
                RefreshGauge = settings.Colors.RefreshGauge.ToHex(),
                ServiceIssue = settings.Colors.ServiceIssue.ToHex(),
                ServiceUnknown = settings.Colors.ServiceUnknown.ToHex(),
                TokenLow = settings.Colors.TokenLow.ToHex(),
                TokenHigh = settings.Colors.TokenHigh.ToHex(),
            },
            Appearance = new AppearanceSettingsDocument
            {
                OverlayWidth = settings.Appearance.OverlayWidth,
                OverlayHeight = settings.Appearance.OverlayHeight,
                GaugePaneWidth = settings.Appearance.GaugePaneWidth,
                GraphBarWidth = settings.Appearance.GraphBarWidth,
                GraphBarGap = settings.Appearance.GraphBarGap,
                StatusStripeWidth = settings.Appearance.StatusStripeWidth,
            },
            Location = new OverlayLocationDocument
            {
                MonitorId = settings.Location.MonitorId,
                X = settings.Location.X,
                Y = settings.Location.Y,
            },
        };
    }

    private sealed class ColorSettingsDocument
    {
        public string? ManaBar { get; set; }

        public string? HpBar { get; set; }

        public string? RefreshGauge { get; set; }

        public string? ServiceIssue { get; set; }

        public string? ServiceUnknown { get; set; }

        public string? TokenLow { get; set; }

        public string? TokenHigh { get; set; }
    }

    private sealed class AppearanceSettingsDocument
    {
        public int? OverlayWidth { get; set; }

        public int? OverlayHeight { get; set; }

        [JsonPropertyName("screenWidth")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? LegacyV2Width { get; set; }

        [JsonPropertyName("screenHeight")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? LegacyV2Height { get; set; }

        public int? GaugePaneWidth { get; set; }

        public int? GraphBarWidth { get; set; }

        public int? GraphBarGap { get; set; }

        public int? StatusStripeWidth { get; set; }
    }

    private sealed class OverlayLocationDocument
    {
        public string? MonitorId { get; set; }

        public double? X { get; set; }

        public double? Y { get; set; }
    }
}
