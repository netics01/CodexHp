using System.Collections;
using System.Globalization;
using System.IO;
using System.Text.Json;
using CodexHp.App.Application;

namespace CodexHp.App.Infrastructure;

public sealed class CodexTokenUsageScanner : ICodexTokenActivitySource
{
    private const long MaxTokenSpreadDurationMs = 5 * 60 * 1000L;
    private const long InitialContextMinimumUncachedInputTokens = 50_000;
    private const long InitialContextTokensPerBucket = 50_000;
    private const int InitialContextMinimumBuckets = 3;
    private const int InitialContextMaximumBuckets = 6;
    private readonly IReadOnlyDictionary<string, string> environment;
    private readonly TokenFileCursorCache fileCache;

    public CodexTokenUsageScanner()
        : this(ReadEnvironment(), new TokenFileCursorCache())
    {
    }

    public CodexTokenUsageScanner(IReadOnlyDictionary<string, string> environment)
        : this(environment, new TokenFileCursorCache())
    {
    }

    public CodexTokenUsageScanner(
        IReadOnlyDictionary<string, string> environment,
        TokenFileCursorCache fileCache)
    {
        this.environment = environment ?? throw new ArgumentNullException(nameof(environment));
        this.fileCache = fileCache ?? throw new ArgumentNullException(nameof(fileCache));
    }

    public IReadOnlyList<int> ReadRecentTokenBuckets(long nowUnixMs, int bucketSeconds, int maxBuckets) =>
        ReadRecentTokenBuckets(
            CodexHome(this.environment),
            nowUnixMs,
            bucketSeconds,
            maxBuckets,
            this.fileCache);

    public static string CodexHome(IReadOnlyDictionary<string, string> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (environment.TryGetValue("CODEX_HOME", out var codexHome) && !string.IsNullOrWhiteSpace(codexHome))
        {
            return codexHome;
        }

        var userProfile = environment.TryGetValue("USERPROFILE", out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, ".codex");
    }

    public static IReadOnlyList<int> ReadRecentTokenBuckets(
        string codexHome,
        long nowUnixMs,
        int bucketSeconds,
        int maxBuckets) =>
        ReadRecentTokenBuckets(
            codexHome,
            nowUnixMs,
            bucketSeconds,
            maxBuckets,
            new TokenFileCursorCache());

    private static IReadOnlyList<int> ReadRecentTokenBuckets(
        string codexHome,
        long nowUnixMs,
        int bucketSeconds,
        int maxBuckets,
        TokenFileCursorCache fileCache)
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
        if (string.IsNullOrWhiteSpace(codexHome) || !Directory.Exists(codexHome))
        {
            return buckets;
        }

        var bucketMs = checked(bucketSeconds * 1000L);
        var windowMs = checked(bucketMs * maxBuckets);
        foreach (var file in EnumerateRecentJsonlFiles(codexHome, nowUnixMs, windowMs))
        {
            AddRecentTokenBuckets(file, nowUnixMs, bucketMs, windowMs, buckets, fileCache);
        }

        return buckets;
    }

    private static IEnumerable<string> EnumerateJsonlFiles(string codexHome)
    {
        foreach (var relativeDirectory in new[] { "sessions", "archived_sessions" })
        {
            var directory = Path.Combine(codexHome, relativeDirectory);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in EnumerateDirectoryJsonlFiles(directory))
            {
                yield return file;
            }
        }
    }

    private static IEnumerable<string> EnumerateRecentJsonlFiles(string codexHome, long nowUnixMs, long windowMs)
    {
        var oldestInterestingWriteUtc = DateTimeOffset.FromUnixTimeMilliseconds(nowUnixMs - windowMs)
            .AddMinutes(-5)
            .UtcDateTime;

        foreach (var file in EnumerateJsonlFiles(codexHome))
        {
            DateTime lastWriteUtc;
            try
            {
                lastWriteUtc = File.GetLastWriteTimeUtc(file);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            if (lastWriteUtc >= oldestInterestingWriteUtc || IsHeldOpen(file))
            {
                yield return file;
            }
        }
    }

    private static bool IsHeldOpen(string file)
    {
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (IOException exception) when ((exception.HResult & 0xFFFF) is 32 or 33)
        {
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static IEnumerable<string> EnumerateDirectoryJsonlFiles(string directory)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
        };

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(directory, "*.jsonl", options);
        }
        catch (IOException)
        {
            yield break;
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var file in files)
        {
            yield return file;
        }
    }

    private static void AddRecentTokenBuckets(
        string file,
        long nowUnixMs,
        long bucketMs,
        long windowMs,
        int[] buckets,
        TokenFileCursorCache fileCache)
    {
        try
        {
            long? activityStartUnixMs = null;
            long? compactionStartUnixMs = null;
            long compactionTokens = 0;
            long? lastCompactionTokenUnixMs = null;
            var tokenUsageEventCount = 0;
            var initialContextApplied = false;
            foreach (var line in fileCache.ReadLines(file))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    using var document = JsonDocument.Parse(line);
                    if (!TryGetTimestampUnixMs(document.RootElement, out var timestampUnixMs))
                    {
                        continue;
                    }

                    if (IsCompactionStart(document.RootElement))
                    {
                        AddUnfinishedCompactionTokens(
                            lastCompactionTokenUnixMs,
                            compactionTokens,
                            nowUnixMs,
                            bucketMs,
                            buckets);
                        compactionStartUnixMs = timestampUnixMs;
                        compactionTokens = 0;
                        lastCompactionTokenUnixMs = null;
                        continue;
                    }

                    if (IsCompactionEnd(document.RootElement))
                    {
                        AddPendingCompactionTokens(
                            compactionStartUnixMs,
                            timestampUnixMs,
                            compactionTokens,
                            nowUnixMs,
                            bucketMs,
                            windowMs,
                            buckets);
                        compactionStartUnixMs = null;
                        compactionTokens = 0;
                        lastCompactionTokenUnixMs = null;
                        continue;
                    }

                    if (!TryFindProperty(document.RootElement, "last_token_usage", out var lastUsage))
                    {
                        if (lastCompactionTokenUnixMs.HasValue)
                        {
                            AddCompletedCompactionTokens(
                                compactionStartUnixMs,
                                lastCompactionTokenUnixMs,
                                compactionTokens,
                                nowUnixMs,
                                bucketMs,
                                windowMs,
                                buckets);
                            compactionStartUnixMs = null;
                            compactionTokens = 0;
                            lastCompactionTokenUnixMs = null;
                        }

                        if (IsActivityStartCandidate(document.RootElement))
                        {
                            activityStartUnixMs = timestampUnixMs;
                        }

                        continue;
                    }

                    var usage = ReadRecentTokenUsage(lastUsage);
                    if (usage.TotalTokens <= 0)
                    {
                        continue;
                    }

                    if (compactionStartUnixMs.HasValue)
                    {
                        if (lastCompactionTokenUnixMs.HasValue)
                        {
                            AddUnfinishedCompactionTokens(
                                lastCompactionTokenUnixMs,
                                compactionTokens,
                                nowUnixMs,
                                bucketMs,
                                buckets);
                            compactionStartUnixMs = null;
                            compactionTokens = 0;
                            lastCompactionTokenUnixMs = null;
                        }
                    }

                    if (compactionStartUnixMs.HasValue)
                    {
                        compactionTokens += usage.TotalTokens;
                        lastCompactionTokenUnixMs = timestampUnixMs;
                        continue;
                    }

                    if (!initialContextApplied
                        && tokenUsageEventCount < 3
                        && IsInitialContextCandidate(usage))
                    {
                        AddInitialContextTokenUsage(
                            timestampUnixMs,
                            activityStartUnixMs,
                            usage,
                            nowUnixMs,
                            bucketMs,
                            windowMs,
                            buckets);
                        initialContextApplied = true;
                    }
                    else
                    {
                        AddRecentTokenUsage(
                            timestampUnixMs,
                            activityStartUnixMs,
                            usage.TotalTokens,
                            nowUnixMs,
                            bucketMs,
                            windowMs,
                            buckets);
                    }

                    tokenUsageEventCount++;
                }
                catch (JsonException)
                {
                    continue;
                }
            }

            AddUnfinishedCompactionTokens(
                lastCompactionTokenUnixMs,
                compactionTokens,
                nowUnixMs,
                bucketMs,
                buckets);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static bool IsActivityStartCandidate(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !TryGetStringProperty(element, "type", out var eventType)
            || eventType != "event_msg"
            || !element.TryGetProperty("payload", out var payload)
            || payload.ValueKind != JsonValueKind.Object
            || !TryGetStringProperty(payload, "type", out var payloadType))
        {
            return false;
        }

        return payloadType is "task_started" or "user_message";
    }

    private static bool IsCompactionStart(JsonElement element) =>
        TryGetStringProperty(element, "type", out var eventType) && eventType == "compacted";

    private static bool IsCompactionEnd(JsonElement element)
    {
        return element.ValueKind == JsonValueKind.Object
            && TryGetStringProperty(element, "type", out var eventType)
            && eventType == "event_msg"
            && element.TryGetProperty("payload", out var payload)
            && payload.ValueKind == JsonValueKind.Object
            && TryGetStringProperty(payload, "type", out var payloadType)
            && payloadType == "context_compacted";
    }

    private static void AddPendingCompactionTokens(
        long? startUnixMs,
        long? endUnixMs,
        long tokens,
        long nowUnixMs,
        long bucketMs,
        long windowMs,
        int[] buckets)
    {
        if (!startUnixMs.HasValue || !endUnixMs.HasValue || tokens <= 0)
        {
            return;
        }

        if (endUnixMs.Value <= startUnixMs.Value)
        {
            AddTokenUsageAtTimestamp(endUnixMs.Value, tokens, nowUnixMs, bucketMs, buckets);
            return;
        }

        AddTokenUsageUniformAcrossWindow(
            startUnixMs.Value,
            endUnixMs.Value,
            tokens,
            nowUnixMs,
            bucketMs,
            windowMs,
            buckets);
    }

    private static void AddCompletedCompactionTokens(
        long? startUnixMs,
        long? endUnixMs,
        long tokens,
        long nowUnixMs,
        long bucketMs,
        long windowMs,
        int[] buckets)
    {
        AddPendingCompactionTokens(
            startUnixMs,
            endUnixMs,
            tokens,
            nowUnixMs,
            bucketMs,
            windowMs,
            buckets);
    }

    private static void AddUnfinishedCompactionTokens(
        long? timestampUnixMs,
        long tokens,
        long nowUnixMs,
        long bucketMs,
        int[] buckets)
    {
        if (timestampUnixMs.HasValue && tokens > 0)
        {
            AddTokenUsageAtTimestamp(timestampUnixMs.Value, tokens, nowUnixMs, bucketMs, buckets);
        }
    }

    private static void AddRecentTokenUsage(
        long timestampUnixMs,
        long? lastActivityUnixMs,
        long tokens,
        long nowUnixMs,
        long bucketMs,
        long windowMs,
        int[] buckets)
    {
        var ageMs = nowUnixMs - timestampUnixMs;
        if (ageMs < 0 || ageMs >= windowMs)
        {
            return;
        }

        if (!lastActivityUnixMs.HasValue
            || lastActivityUnixMs.Value >= timestampUnixMs
            || timestampUnixMs - lastActivityUnixMs.Value < bucketMs)
        {
            AddTokenUsageAtTimestamp(timestampUnixMs, tokens, nowUnixMs, bucketMs, buckets);
            return;
        }

        var durationMs = Math.Min(timestampUnixMs - lastActivityUnixMs.Value, MaxTokenSpreadDurationMs);
        AddTokenUsageAcrossWindow(
            timestampUnixMs - durationMs,
            timestampUnixMs,
            tokens,
            nowUnixMs,
            bucketMs,
            windowMs,
            buckets);
    }

    private static bool IsInitialContextCandidate(RecentTokenUsage usage)
    {
        return usage.HasDetailedInput
            && usage.UncachedInputTokens >= InitialContextMinimumUncachedInputTokens
            && usage.CachedInputTokens * 2 <= usage.InputTokens;
    }

    private static void AddInitialContextTokenUsage(
        long timestampUnixMs,
        long? activityStartUnixMs,
        RecentTokenUsage usage,
        long nowUnixMs,
        long bucketMs,
        long windowMs,
        int[] buckets)
    {
        var ageMs = nowUnixMs - timestampUnixMs;
        if (ageMs < 0 || ageMs >= windowMs)
        {
            return;
        }

        var observedDurationMs = activityStartUnixMs.HasValue && activityStartUnixMs.Value < timestampUnixMs
            ? timestampUnixMs - activityStartUnixMs.Value
            : 0;
        var minimumDurationMs = InitialContextMinimumDurationMs(usage.UncachedInputTokens, bucketMs);
        var durationMs = Math.Min(Math.Max(observedDurationMs, minimumDurationMs), MaxTokenSpreadDurationMs);
        if (durationMs <= 0)
        {
            AddTokenUsageAtTimestamp(timestampUnixMs, usage.TotalTokens, nowUnixMs, bucketMs, buckets);
            return;
        }

        AddTokenUsageAcrossWindow(
            timestampUnixMs - durationMs,
            timestampUnixMs,
            usage.UncachedInputTokens,
            nowUnixMs,
            bucketMs,
            windowMs,
            buckets,
            SpreadRampDirection.FrontLoaded);

        var remainingTokens = usage.TotalTokens - usage.UncachedInputTokens;
        if (remainingTokens > 0)
        {
            AddTokenUsageAtTimestamp(timestampUnixMs, remainingTokens, nowUnixMs, bucketMs, buckets);
        }
    }

    private static long InitialContextMinimumDurationMs(long uncachedInputTokens, long bucketMs)
    {
        var estimatedBuckets = (int)Math.Ceiling(uncachedInputTokens / (double)InitialContextTokensPerBucket);
        var clampedBuckets = Math.Clamp(estimatedBuckets, InitialContextMinimumBuckets, InitialContextMaximumBuckets);
        return clampedBuckets * bucketMs;
    }

    private static void AddTokenUsageAtTimestamp(
        long timestampUnixMs,
        long tokens,
        long nowUnixMs,
        long bucketMs,
        int[] buckets)
    {
        var ageMs = nowUnixMs - timestampUnixMs;
        if (ageMs < 0 || ageMs >= bucketMs * buckets.Length)
        {
            return;
        }

        var bucketFromNewest = (int)(ageMs / bucketMs);
        var bucketIndex = buckets.Length - 1 - bucketFromNewest;
        buckets[bucketIndex] = AddClamped(buckets[bucketIndex], tokens);
    }

    private static void AddTokenUsageAcrossWindow(
        long startUnixMs,
        long endUnixMs,
        long tokens,
        long nowUnixMs,
        long bucketMs,
        long windowMs,
        int[] buckets,
        SpreadRampDirection direction = SpreadRampDirection.BackLoaded)
    {
        var windowStartUnixMs = nowUnixMs - windowMs;
        var clampedStartUnixMs = Math.Max(startUnixMs, windowStartUnixMs);
        var clampedEndUnixMs = Math.Min(endUnixMs, nowUnixMs);
        if (clampedEndUnixMs <= clampedStartUnixMs)
        {
            AddTokenUsageAtTimestamp(endUnixMs, tokens, nowUnixMs, bucketMs, buckets);
            return;
        }

        var durationMs = Math.Max(1, endUnixMs - startUnixMs);
        var segments = new List<(int BucketIndex, long OverlapMs)>();
        for (var index = 0; index < buckets.Length; index++)
        {
            var bucketStartUnixMs = windowStartUnixMs + (index * bucketMs);
            var bucketEndUnixMs = bucketStartUnixMs + bucketMs;
            var overlapMs = Math.Min(clampedEndUnixMs, bucketEndUnixMs)
                - Math.Max(clampedStartUnixMs, bucketStartUnixMs);
            if (overlapMs > 0)
            {
                segments.Add((index, overlapMs));
            }
        }

        var totalRampWeight = 0L;
        for (var index = 0; index < segments.Count; index++)
        {
            totalRampWeight += segments[index].OverlapMs * RampWeight(index, segments.Count, direction);
        }

        var uniformBudget = tokens * 0.25d;
        var rampBudget = tokens - uniformBudget;
        long assignedTokens = 0;
        var remainderBucketIndex = -1;
        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];
            var rampWeight = RampWeight(index, segments.Count, direction);
            var uniformTokens = uniformBudget * segment.OverlapMs / durationMs;
            var rampTokens = totalRampWeight > 0
                ? rampBudget * (segment.OverlapMs * rampWeight) / totalRampWeight
                : 0;
            var bucketTokens = (long)Math.Floor(uniformTokens + rampTokens);
            if (bucketTokens <= 0)
            {
                continue;
            }

            buckets[segment.BucketIndex] = AddClamped(buckets[segment.BucketIndex], bucketTokens);
            assignedTokens += bucketTokens;
            remainderBucketIndex = direction == SpreadRampDirection.FrontLoaded
                ? segments[0].BucketIndex
                : segment.BucketIndex;
        }

        var fullyVisible = clampedStartUnixMs == startUnixMs && clampedEndUnixMs == endUnixMs;
        var remainder = fullyVisible ? tokens - assignedTokens : 0;
        if (remainder > 0 && remainderBucketIndex >= 0)
        {
            buckets[remainderBucketIndex] = AddClamped(buckets[remainderBucketIndex], remainder);
        }
    }

    private static long RampWeight(int segmentIndex, int segmentCount, SpreadRampDirection direction) =>
        direction == SpreadRampDirection.FrontLoaded ? segmentCount - segmentIndex : segmentIndex;

    private static void AddTokenUsageUniformAcrossWindow(
        long startUnixMs,
        long endUnixMs,
        long tokens,
        long nowUnixMs,
        long bucketMs,
        long windowMs,
        int[] buckets)
    {
        var windowStartUnixMs = nowUnixMs - windowMs;
        var clampedStartUnixMs = Math.Max(startUnixMs, windowStartUnixMs);
        var clampedEndUnixMs = Math.Min(endUnixMs, nowUnixMs);
        if (clampedEndUnixMs <= clampedStartUnixMs)
        {
            AddTokenUsageAtTimestamp(endUnixMs, tokens, nowUnixMs, bucketMs, buckets);
            return;
        }

        var durationMs = Math.Max(1, endUnixMs - startUnixMs);
        long assignedTokens = 0;
        var lastBucketIndex = -1;
        for (var index = 0; index < buckets.Length; index++)
        {
            var bucketStartUnixMs = windowStartUnixMs + (index * bucketMs);
            var bucketEndUnixMs = bucketStartUnixMs + bucketMs;
            var overlapMs = Math.Min(clampedEndUnixMs, bucketEndUnixMs)
                - Math.Max(clampedStartUnixMs, bucketStartUnixMs);
            if (overlapMs <= 0)
            {
                continue;
            }

            var bucketTokens = (long)Math.Floor(tokens * (double)overlapMs / durationMs);
            if (bucketTokens <= 0)
            {
                continue;
            }

            buckets[index] = AddClamped(buckets[index], bucketTokens);
            assignedTokens += bucketTokens;
            lastBucketIndex = index;
        }

        var fullyVisible = clampedStartUnixMs == startUnixMs && clampedEndUnixMs == endUnixMs;
        var remainder = fullyVisible ? tokens - assignedTokens : 0;
        if (remainder > 0 && lastBucketIndex >= 0)
        {
            buckets[lastBucketIndex] = AddClamped(buckets[lastBucketIndex], remainder);
        }
    }

    private static bool TryFindProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals(propertyName))
                {
                    value = property.Value;
                    return true;
                }

                if (TryFindProperty(property.Value, propertyName, out value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (TryFindProperty(item, propertyName, out value))
                {
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static RecentTokenUsage ReadRecentTokenUsage(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var number))
        {
            var total = Math.Max(0, number);
            return new RecentTokenUsage(total, 0, 0, 0, false);
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return new RecentTokenUsage(0, 0, 0, 0, false);
        }

        var inputTokens = TryGetInt64Property(element, "input_tokens", out var input) ? Math.Max(0, input) : 0;
        var cachedInputTokens = TryGetInt64Property(element, "cached_input_tokens", out var cached) ? Math.Max(0, cached) : 0;
        var outputTokens = TryGetInt64Property(element, "output_tokens", out var output) ? Math.Max(0, output) : 0;
        var uncachedInputTokens = Math.Max(0, inputTokens - cachedInputTokens);

        if (inputTokens > 0 || outputTokens > 0)
        {
            return new RecentTokenUsage(
                uncachedInputTokens + outputTokens,
                inputTokens,
                cachedInputTokens,
                uncachedInputTokens,
                true);
        }

        var fallbackTotal = TryGetInt64Property(element, "total_tokens", out var totalTokens)
            ? Math.Max(0, totalTokens)
            : 0;
        return new RecentTokenUsage(fallbackTotal, 0, 0, 0, false);
    }

    private static bool TryGetInt64Property(JsonElement element, string propertyName, out long value)
    {
        if (element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Number)
        {
            return property.TryGetInt64(out value);
        }

        value = 0;
        return false;
    }

    private static bool TryGetStringProperty(JsonElement element, string propertyName, out string value)
    {
        if (element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString() ?? string.Empty;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool TryGetTimestampUnixMs(JsonElement element, out long unixMs)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("timestamp", out var timestamp)
            && timestamp.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(
                timestamp.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var dateTime))
        {
            unixMs = dateTime.ToUnixTimeMilliseconds();
            return true;
        }

        unixMs = 0;
        return false;
    }

    private static int AddClamped(int current, long value) =>
        value >= int.MaxValue - current ? int.MaxValue : current + (int)value;

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

    private enum SpreadRampDirection
    {
        BackLoaded,
        FrontLoaded,
    }

    private readonly record struct RecentTokenUsage(
        long TotalTokens,
        long InputTokens,
        long CachedInputTokens,
        long UncachedInputTokens,
        bool HasDetailedInput);
}
