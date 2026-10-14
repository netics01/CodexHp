using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using CodexHp.App.Application;
using CodexHp.Core.Domain;

namespace CodexHp.App.Infrastructure;

public interface IClaudeUsageSource
{
    // Null when the Claude desktop app has not recorded any usage on this PC.
    UsageSnapshot? Read(long nowUnixMs);
}

// Reads only values the Claude desktop app already stored: its plan-usage history file and the
// cached response of its own usage request. It never reads Claude credentials or cookies.
public sealed class ClaudeDesktopUsageSource : IClaudeUsageSource
{
    public const string PackageFamilyName = "Claude_pzs8sxrjxfjjc";
    public const string HistorySource = "app history";
    public const string CacheSource = "app cache";
    private const string HistoryFileName = "plan-usage-history.json";
    private const string UsageUrlPrefix = "https://claude.ai/api/organizations/";
    private const string UsageUrlSuffix = "/usage";
    private const int SessionWindowSeconds = 18_000;
    private const int WeeklyWindowSeconds = 604_800;
    private const long MaxHistoryBytes = 4 * 1024 * 1024;
    private readonly IReadOnlyList<string> appDataDirectories;
    private readonly IDiagnosticLogger? logger;

    public ClaudeDesktopUsageSource(IDiagnosticLogger? logger = null)
        : this(DefaultAppDataDirectories(ReadEnvironment()), logger)
    {
    }

    public ClaudeDesktopUsageSource(IReadOnlyList<string> appDataDirectories, IDiagnosticLogger? logger = null)
    {
        this.appDataDirectories = appDataDirectories ?? throw new ArgumentNullException(nameof(appDataDirectories));
        this.logger = logger;
    }

    public static IReadOnlyList<string> DefaultAppDataDirectories(IReadOnlyDictionary<string, string> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        var directories = new List<string>();
        if (environment.TryGetValue("APPDATA", out var roaming) && !string.IsNullOrWhiteSpace(roaming))
        {
            directories.Add(Path.Combine(roaming, "Claude"));
        }

        // MSIX installs may virtualize Roaming writes into the package's LocalCache.
        if (environment.TryGetValue("LOCALAPPDATA", out var local) && !string.IsNullOrWhiteSpace(local))
        {
            directories.Add(Path.Combine(local, "Packages", PackageFamilyName, "LocalCache", "Roaming", "Claude"));
        }

        return directories;
    }

    public UsageSnapshot? Read(long nowUnixMs)
    {
        var directory = this.SelectAppDataDirectory();
        if (directory is null)
        {
            return null;
        }

        var history = ReadLatestHistorySample(Path.Combine(directory, HistoryFileName));
        CachedUsage? cached = null;
        try
        {
            cached = ReadLatestCachedUsage(Path.Combine(directory, "Cache", "Cache_Data"), history?.Organization, nowUnixMs);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ChromiumCacheFormatException or JsonException or InvalidDataException)
        {
            // The cache only adds reset times and fresher percentages; history still works without it.
            this.logger?.Log(DiagnosticLevel.Warning, "ClaudeUsage", "The Claude desktop cache could not be read.", exception);
        }

        return Merge(history, cached);
    }

    internal static UsageSnapshot? Merge(HistorySample? history, CachedUsage? cached)
    {
        if (cached is not null && (history is null || cached.ObservedUnixMs >= history.ObservedUnixMs))
        {
            return CreateSnapshot(
                cached.SessionUsedPercent,
                cached.WeeklyUsedPercent,
                cached.SessionResetUnixMs ?? 0,
                cached.WeeklyResetUnixMs ?? 0,
                cached.ObservedUnixMs,
                CacheSource);
        }

        if (history is null)
        {
            return null;
        }

        // A reset time from an older response still applies if that window has not rolled over
        // by the time of the newer history sample.
        long ValidReset(long? reset) => reset is { } value && value > history.ObservedUnixMs ? value : 0;
        return CreateSnapshot(
            history.SessionUsedPercent,
            history.WeeklyUsedPercent,
            ValidReset(cached?.SessionResetUnixMs),
            ValidReset(cached?.WeeklyResetUnixMs),
            history.ObservedUnixMs,
            HistorySource);
    }

    private static UsageSnapshot CreateSnapshot(
        int sessionUsed,
        int weeklyUsed,
        long sessionResetUnixMs,
        long weeklyResetUnixMs,
        long observedUnixMs,
        string source) =>
        new(
            Math.Clamp(100 - sessionUsed, 0, 100),
            Math.Clamp(100 - weeklyUsed, 0, 100),
            sessionResetUnixMs,
            SessionWindowSeconds,
            weeklyResetUnixMs,
            WeeklyWindowSeconds)
        {
            ObservedUnixMs = observedUnixMs,
            Source = source,
        };

    private string? SelectAppDataDirectory()
    {
        string? selected = null;
        var newest = DateTime.MinValue;
        foreach (var directory in this.appDataDirectories)
        {
            var history = new FileInfo(Path.Combine(directory, HistoryFileName));
            var cacheIndex = new FileInfo(Path.Combine(directory, "Cache", "Cache_Data", "index"));
            var written = new[] { history, cacheIndex }
                .Where(file => file.Exists)
                .Select(file => file.LastWriteTimeUtc)
                .DefaultIfEmpty(DateTime.MinValue)
                .Max();
            if (written > newest)
            {
                newest = written;
                selected = directory;
            }
        }

        return selected;
    }

    internal static HistorySample? ReadLatestHistorySample(string path)
    {
        string json;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > MaxHistoryBytes)
            {
                return null;
            }

            using var reader = new StreamReader(stream, Encoding.UTF8);
            json = reader.ReadToEnd();
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }

        return ParseLatestHistorySample(json);
    }

    internal static HistorySample? ParseLatestHistorySample(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("samples", out var samples) || samples.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            // Choose the latest observation and its organization; never mix windows across samples.
            HistorySample? latest = null;
            foreach (var sample in samples.EnumerateArray())
            {
                if (sample.ValueKind != JsonValueKind.Object
                    || !TryGetInt64(sample, "t", out var observedUnixMs)
                    || observedUnixMs <= 0
                    || !sample.TryGetProperty("org", out var organization)
                    || organization.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(organization.GetString())
                    || !sample.TryGetProperty("u", out var usage)
                    || usage.ValueKind != JsonValueKind.Object
                    || TryGetPercent(usage, "fh") is not { } sessionUsed
                    || TryGetPercent(usage, "sd") is not { } weeklyUsed)
                {
                    continue;
                }

                if (latest is null || observedUnixMs >= latest.ObservedUnixMs)
                {
                    latest = new HistorySample(organization.GetString()!, observedUnixMs, sessionUsed, weeklyUsed);
                }
            }

            return latest;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static CachedUsage? ReadLatestCachedUsage(string cacheDirectory, string? organization, long nowUnixMs)
    {
        if (!Directory.Exists(cacheDirectory))
        {
            return null;
        }

        var responses = ChromiumBlockFileCache.ReadResponses(cacheDirectory, key => ParseOrganization(key) is { } candidate
            && (organization is null || string.Equals(candidate, organization, StringComparison.OrdinalIgnoreCase)));
        CachedUsage? latest = null;
        foreach (var response in responses)
        {
            if (response.StatusCode != 200
                || response.ResponseUnixMs <= 0
                || response.ResponseUnixMs > nowUnixMs + 60_000
                || (latest is not null && response.ResponseUnixMs <= latest.ObservedUnixMs))
            {
                continue;
            }

            var body = ChromiumBlockFileCache.DecodeBody(response.Body, response.Headers);
            if (ParseUsageResponse(body, response.ResponseUnixMs) is { } usage)
            {
                latest = usage;
            }
        }

        return latest;
    }

    // Cache keys carry a partition prefix such as "1/0/" before the URL.
    internal static string? ParseOrganization(string key)
    {
        var start = key.IndexOf(UsageUrlPrefix, StringComparison.Ordinal);
        if (start < 0 || (start > 0 && key[start - 1] is not '/' and not ' ')
            || !key.EndsWith(UsageUrlSuffix, StringComparison.Ordinal))
        {
            return null;
        }

        var organization = key[(start + UsageUrlPrefix.Length)..^UsageUrlSuffix.Length];
        return organization.Length is > 0 and <= 64 && organization.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')
            ? organization
            : null;
    }

    internal static CachedUsage? ParseUsageResponse(byte[] body, long observedUnixMs)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || ReadWindow(root, "five_hour") is not { } session
                || ReadWindow(root, "seven_day") is not { } weekly)
            {
                return null;
            }

            return new CachedUsage(observedUnixMs, session.UsedPercent, weekly.UsedPercent, session.ResetUnixMs, weekly.ResetUnixMs);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static (int UsedPercent, long? ResetUnixMs)? ReadWindow(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var window) || window.ValueKind != JsonValueKind.Object
            || TryGetPercent(window, "utilization") is not { } used)
        {
            return null;
        }

        long? reset = window.TryGetProperty("resets_at", out var resetsAt)
            && resetsAt.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(resetsAt.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
                ? parsed.ToUnixTimeMilliseconds()
                : null;
        return (used, reset is > 0 ? reset : null);
    }

    private static int? TryGetPercent(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out var number) && double.IsFinite(number)
            ? (int)Math.Clamp(Math.Round(number, MidpointRounding.AwayFromZero), 0, 100)
            : null;

    private static bool TryGetInt64(JsonElement element, string name, out long value)
    {
        value = 0;
        return element.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt64(out value);
    }

    private static IReadOnlyDictionary<string, string> ReadEnvironment()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry item in Environment.GetEnvironmentVariables())
        {
            if (item.Key is string key && item.Value is string value)
            {
                result[key] = value;
            }
        }

        return result;
    }

    internal sealed record HistorySample(string Organization, long ObservedUnixMs, int SessionUsedPercent, int WeeklyUsedPercent);

    internal sealed record CachedUsage(
        long ObservedUnixMs,
        int SessionUsedPercent,
        int WeeklyUsedPercent,
        long? SessionResetUnixMs,
        long? WeeklyResetUnixMs);
}
