using System.Collections;
using System.Globalization;
using System.IO;
using System.Text.Json;
using CodexHp.App.Application;

namespace CodexHp.App.Infrastructure;

// Builds the token activity graph from Claude Code session logs (CLI and the desktop Code tab).
// Counts the same meaning as the Codex graph: newly processed input plus output. Cache reads are
// excluded; cache writes are new input for that request.
public sealed class ClaudeTokenUsageScanner : ITokenActivitySource
{
    private readonly IReadOnlyDictionary<string, string> environment;
    private readonly TokenFileCursorCache fileCache;

    public ClaudeTokenUsageScanner()
        : this(ReadEnvironment(), new TokenFileCursorCache())
    {
    }

    public ClaudeTokenUsageScanner(IReadOnlyDictionary<string, string> environment, TokenFileCursorCache? fileCache = null)
    {
        this.environment = environment ?? throw new ArgumentNullException(nameof(environment));
        this.fileCache = fileCache ?? new TokenFileCursorCache();
    }

    public static string ClaudeConfigDirectory(IReadOnlyDictionary<string, string> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (environment.TryGetValue("CLAUDE_CONFIG_DIR", out var configured) && !string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var userProfile = environment.TryGetValue("USERPROFILE", out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, ".claude");
    }

    public IReadOnlyList<int> ReadRecentTokenBuckets(long nowUnixMs, int bucketSeconds, int maxBuckets)
    {
        if (bucketSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bucketSeconds));
        }

        if (maxBuckets <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBuckets));
        }

        var buckets = new int[maxBuckets];
        var projects = Path.Combine(ClaudeConfigDirectory(this.environment), "projects");
        if (!Directory.Exists(projects))
        {
            return buckets;
        }

        var bucketMs = checked(bucketSeconds * 1000L);
        var windowMs = checked(bucketMs * maxBuckets);
        var oldestWriteUtc = DateTimeOffset.FromUnixTimeMilliseconds(nowUnixMs - windowMs).AddMinutes(-5).UtcDateTime;
        // One response can be logged once per content block; keep its largest usage once.
        var responses = new Dictionary<string, (long TimestampUnixMs, long Tokens)>(StringComparer.Ordinal);
        foreach (var file in EnumerateRecentFiles(projects, oldestWriteUtc))
        {
            this.CollectResponses(file, nowUnixMs - windowMs, responses);
        }

        foreach (var (timestampUnixMs, tokens) in responses.Values)
        {
            var ageMs = nowUnixMs - timestampUnixMs;
            if (ageMs < 0 || ageMs >= windowMs || tokens <= 0)
            {
                continue;
            }

            var index = buckets.Length - 1 - (int)(ageMs / bucketMs);
            buckets[index] = tokens >= int.MaxValue - buckets[index] ? int.MaxValue : buckets[index] + (int)tokens;
        }

        return buckets;
    }

    private void CollectResponses(
        string file,
        long windowStartUnixMs,
        Dictionary<string, (long TimestampUnixMs, long Tokens)> responses)
    {
        IReadOnlyList<string> lines;
        try
        {
            lines = this.fileCache.ReadLines(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var line in lines)
        {
            if (!line.Contains("\"usage\"", StringComparison.Ordinal)
                || !line.Contains("\"assistant\"", StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(line);
                if (ReadResponse(document.RootElement) is not { } response || response.TimestampUnixMs < windowStartUnixMs)
                {
                    continue;
                }

                if (!responses.TryGetValue(response.Key, out var existing) || response.Tokens > existing.Tokens)
                {
                    responses[response.Key] = (response.TimestampUnixMs, response.Tokens);
                }
            }
            catch (JsonException)
            {
            }
        }
    }

    internal static (string Key, long TimestampUnixMs, long Tokens)? ReadResponse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("type", out var type)
            || type.ValueKind != JsonValueKind.String
            || type.GetString() != "assistant"
            || !root.TryGetProperty("timestamp", out var timestamp)
            || timestamp.ValueKind != JsonValueKind.String
            || !DateTimeOffset.TryParse(timestamp.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time)
            || !root.TryGetProperty("message", out var message)
            || message.ValueKind != JsonValueKind.Object
            || !message.TryGetProperty("usage", out var usage)
            || usage.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var tokens = Read(usage, "input_tokens") + Read(usage, "cache_creation_input_tokens") + Read(usage, "output_tokens");
        var messageId = message.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null;
        var requestId = root.TryGetProperty("requestId", out var request) && request.ValueKind == JsonValueKind.String
            ? request.GetString()
            : null;
        var uuid = root.TryGetProperty("uuid", out var entryId) && entryId.ValueKind == JsonValueKind.String
            ? entryId.GetString()
            : null;
        var key = messageId is not null ? $"{messageId}|{requestId}" : uuid;
        return key is null ? null : (key, time.ToUnixTimeMilliseconds(), tokens);
    }

    private static long Read(JsonElement usage, string name) =>
        usage.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? Math.Max(0, number)
            : 0;

    private static IEnumerable<string> EnumerateRecentFiles(string directory, DateTime oldestWriteUtc)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(directory, "*.jsonl", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var file in files)
        {
            DateTime written;
            try
            {
                written = File.GetLastWriteTimeUtc(file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (written >= oldestWriteUtc)
            {
                yield return file;
            }
        }
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
}
