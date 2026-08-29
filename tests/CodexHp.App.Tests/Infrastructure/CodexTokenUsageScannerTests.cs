using CodexHp.App.Infrastructure;
using Xunit;

namespace CodexHp.App.Tests.Infrastructure;

public sealed class CodexTokenUsageScannerTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "CodexHp.Tests",
        Guid.NewGuid().ToString("N"));

    public CodexTokenUsageScannerTests()
    {
        Directory.CreateDirectory(this.root);
    }

    [Fact]
    public void ReadRecentTokenBuckets_groups_events_by_fixed_windows()
    {
        this.WriteSession(
            TokenCountLine("2026-05-25T15:17:24.999Z", 99),
            TokenCountLine("2026-05-25T15:17:35.000Z", 3),
            TokenCountLine("2026-05-25T15:17:45.000Z", 7),
            TokenCountLine("2026-05-25T15:17:55.000Z", 11),
            "{ invalid json",
            "{\"timestamp\":\"2026-05-25T15:17:58.000Z\",\"message\":\"not token usage\"}");

        var buckets = CodexTokenUsageScanner.ReadRecentTokenBuckets(
            this.root,
            UnixMs("2026-05-25T15:18:00.000Z"),
            bucketSeconds: 10,
            maxBuckets: 3);

        Assert.Equal([3, 7, 11], buckets);
    }

    [Fact]
    public void ReadRecentTokenBuckets_reads_active_file_open_for_writing()
    {
        var file = this.WriteSession(TokenCountLine("2026-05-25T15:17:55.000Z", 11));
        using var activeWriter = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        var buckets = CodexTokenUsageScanner.ReadRecentTokenBuckets(
            this.root,
            UnixMs("2026-05-25T15:18:00.000Z"),
            10,
            3);

        Assert.Equal([0, 0, 11], buckets);
    }

    [Fact]
    public void ReadRecentTokenBuckets_reads_active_file_when_its_last_write_time_is_stale()
    {
        var file = this.WriteSession(TokenCountLine("2026-05-25T15:17:55.000Z", 11));
        File.SetLastWriteTimeUtc(file, DateTimeOffset.Parse("2026-05-25T14:00:00.000Z").UtcDateTime);
        using var activeWriter = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        var buckets = CodexTokenUsageScanner.ReadRecentTokenBuckets(
            this.root,
            UnixMs("2026-05-25T15:18:00.000Z"),
            10,
            3);

        Assert.Equal([0, 0, 11], buckets);
    }

    [Fact]
    public void ReadRecentTokenBuckets_reads_archived_sessions()
    {
        this.WriteSession(TokenCountLine("2026-05-25T15:17:55.000Z", 11));
        var archived = Path.Combine(this.root, "archived_sessions");
        Directory.CreateDirectory(archived);
        File.WriteAllText(
            Path.Combine(archived, "archived.jsonl"),
            TokenCountLine("2026-05-25T15:17:45.000Z", 7) + Environment.NewLine);

        var buckets = CodexTokenUsageScanner.ReadRecentTokenBuckets(
            this.root,
            UnixMs("2026-05-25T15:18:00.000Z"),
            10,
            3);

        Assert.Equal([0, 7, 11], buckets);
    }

    [Fact]
    public void ReadRecentTokenBuckets_excludes_cached_input()
    {
        this.WriteSession(
            TokenUsageLine(
                "2026-05-25T15:17:55.000Z",
                inputTokens: 140_691,
                cachedInputTokens: 136_576,
                outputTokens: 727));

        var buckets = CodexTokenUsageScanner.ReadRecentTokenBuckets(
            this.root,
            UnixMs("2026-05-25T15:18:00.000Z"),
            10,
            3);

        Assert.Equal([0, 0, 4_842], buckets);
    }

    [Fact]
    public void ReadRecentTokenBuckets_spreads_regular_activity_with_back_loaded_ramp()
    {
        this.WriteSession(
            ActivityLine("2026-05-25T15:17:20.000Z"),
            TokenCountLine("2026-05-25T15:17:50.000Z", 90));

        var buckets = CodexTokenUsageScanner.ReadRecentTokenBuckets(
            this.root,
            UnixMs("2026-05-25T15:18:00.000Z"),
            10,
            5);

        Assert.Equal([0, 7, 30, 53, 0], buckets);
    }

    [Fact]
    public void ReadRecentTokenBuckets_spreads_initial_uncached_context_with_front_loaded_ramp()
    {
        this.WriteSession(
            ActivityLine("2026-05-25T15:17:50.000Z"),
            TokenUsageLine("2026-05-25T15:18:00.000Z", 120_000, 0, 0));

        var buckets = CodexTokenUsageScanner.ReadRecentTokenBuckets(
            this.root,
            UnixMs("2026-05-25T15:18:10.000Z"),
            10,
            6);

        Assert.Equal([0, 0, 55_000, 40_000, 25_000, 0], buckets);
    }

    [Fact]
    public void ReadRecentTokenBuckets_does_not_reset_activity_start_for_completion_events()
    {
        this.WriteSession(
            ActivityLine("2026-05-25T15:17:20.000Z"),
            EventLine("2026-05-25T15:17:45.000Z", "reasoning"),
            EventLine("2026-05-25T15:17:49.000Z", "agent_message"),
            EventLine("2026-05-25T15:17:50.000Z", "message"),
            TokenCountLine("2026-05-25T15:17:50.000Z", 90));

        var buckets = CodexTokenUsageScanner.ReadRecentTokenBuckets(
            this.root,
            UnixMs("2026-05-25T15:18:00.000Z"),
            10,
            5);

        Assert.Equal([0, 7, 30, 53, 0], buckets);
    }

    [Fact]
    public void ReadRecentTokenBuckets_prefers_task_started_over_earlier_user_message()
    {
        this.WriteSession(
            ActivityLine("2026-05-25T15:17:00.000Z"),
            EventLine("2026-05-25T15:17:20.000Z", "task_started"),
            TokenCountLine("2026-05-25T15:17:50.000Z", 90));

        var buckets = CodexTokenUsageScanner.ReadRecentTokenBuckets(
            this.root,
            UnixMs("2026-05-25T15:18:00.000Z"),
            10,
            5);

        Assert.Equal([0, 7, 30, 53, 0], buckets);
    }

    [Fact]
    public void ReadRecentTokenBuckets_caps_spread_to_five_minutes()
    {
        this.WriteSession(
            ActivityLine("2026-05-25T15:00:00.000Z"),
            TokenCountLine("2026-05-25T15:10:00.000Z", 300));

        var buckets = CodexTokenUsageScanner.ReadRecentTokenBuckets(
            this.root,
            UnixMs("2026-05-25T15:10:00.000Z"),
            60,
            10);

        Assert.Equal([0, 0, 0, 0, 0, 15, 37, 60, 82, 106], buckets);
    }

    [Fact]
    public void ReadRecentTokenBuckets_spreads_compaction_tokens_uniformly()
    {
        this.WriteSession(
            "{\"timestamp\":\"2026-05-25T15:17:20.000Z\",\"type\":\"compacted\",\"payload\":{\"replacement_history\":[{\"last_token_usage\":{\"total_tokens\":999}}]}}",
            TokenCountLine("2026-05-25T15:17:35.000Z", 90),
            EventLine("2026-05-25T15:17:50.000Z", "context_compacted"));

        var buckets = CodexTokenUsageScanner.ReadRecentTokenBuckets(
            this.root,
            UnixMs("2026-05-25T15:18:00.000Z"),
            10,
            5);

        Assert.Equal([0, 30, 30, 30, 0], buckets);
    }

    [Fact]
    public void ReadRecentTokenBuckets_keeps_compaction_open_through_metadata_before_its_token_count()
    {
        this.WriteSession(
            "{\"timestamp\":\"2026-05-25T15:17:20.000Z\",\"type\":\"compacted\",\"payload\":{\"replacement_history\":[]}}",
            "{\"timestamp\":\"2026-05-25T15:17:21.000Z\",\"type\":\"world_state\",\"payload\":{}}",
            "{\"timestamp\":\"2026-05-25T15:17:22.000Z\",\"type\":\"turn_context\",\"payload\":{}}",
            TokenCountLine("2026-05-25T15:17:35.000Z", 90),
            EventLine("2026-05-25T15:17:50.000Z", "context_compacted"));

        var buckets = CodexTokenUsageScanner.ReadRecentTokenBuckets(
            this.root,
            UnixMs("2026-05-25T15:18:00.000Z"),
            10,
            5);

        Assert.Equal([0, 30, 30, 30, 0], buckets);
    }

    [Fact]
    public void ReadRecentTokenBuckets_finalizes_current_compaction_format_after_the_first_non_token_event()
    {
        this.WriteSession(
            "{\"timestamp\":\"2026-05-25T15:17:20.000Z\",\"type\":\"compacted\",\"payload\":{\"replacement_history\":[]}}",
            TokenCountLine("2026-05-25T15:17:30.000Z", 120),
            EventLine("2026-05-25T15:17:31.000Z", "item_completed"),
            TokenCountLine("2026-05-25T15:17:50.000Z", 90));

        var buckets = CodexTokenUsageScanner.ReadRecentTokenBuckets(
            this.root,
            UnixMs("2026-05-25T15:18:00.000Z"),
            10,
            5);

        Assert.Equal([0, 120, 0, 90, 0], buckets);
    }

    [Fact]
    public void ReadRecentTokenBuckets_does_not_uniformly_spread_an_unfinished_compaction_at_end_of_file()
    {
        this.WriteSession(
            "{\"timestamp\":\"2026-05-25T15:17:20.000Z\",\"type\":\"compacted\",\"payload\":{\"replacement_history\":[]}}",
            TokenCountLine("2026-05-25T15:17:35.000Z", 90));

        var buckets = CodexTokenUsageScanner.ReadRecentTokenBuckets(
            this.root,
            UnixMs("2026-05-25T15:18:00.000Z"),
            10,
            5);

        Assert.Equal([0, 0, 90, 0, 0], buckets);
    }

    [Fact]
    public void ReadRecentTokenBuckets_ignores_old_files()
    {
        var oldFile = this.WriteNamedSession("old.jsonl", TokenCountLine("2026-05-25T15:17:55.000Z", 99));
        var recentFile = this.WriteNamedSession("recent.jsonl", TokenCountLine("2026-05-25T15:17:55.000Z", 11));
        File.SetLastWriteTimeUtc(oldFile, DateTimeOffset.Parse("2026-05-25T14:00:00.000Z").UtcDateTime);
        File.SetLastWriteTimeUtc(recentFile, DateTimeOffset.Parse("2026-05-25T15:17:58.000Z").UtcDateTime);

        var buckets = CodexTokenUsageScanner.ReadRecentTokenBuckets(
            this.root,
            UnixMs("2026-05-25T15:18:00.000Z"),
            10,
            3);

        Assert.Equal([0, 0, 11], buckets);
    }

    [Fact]
    public void CodexHome_prefers_codex_home_and_falls_back_to_user_profile()
    {
        Assert.Equal(
            @"D:\CodexHome",
            CodexTokenUsageScanner.CodexHome(new Dictionary<string, string>
            {
                ["CODEX_HOME"] = @"D:\CodexHome",
                ["USERPROFILE"] = @"D:\Users\Me",
            }));
        Assert.Equal(
            @"D:\Users\Me\.codex",
            CodexTokenUsageScanner.CodexHome(new Dictionary<string, string>
            {
                ["USERPROFILE"] = @"D:\Users\Me",
            }));
    }

    public void Dispose()
    {
        if (Directory.Exists(this.root))
        {
            Directory.Delete(this.root, recursive: true);
        }
    }

    private string WriteSession(params string[] lines) => this.WriteNamedSession("session.jsonl", lines);

    private string WriteNamedSession(string fileName, params string[] lines)
    {
        var directory = Path.Combine(this.root, "sessions", "2026", "05", "25");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        File.WriteAllLines(path, lines);
        return path;
    }

    private static long UnixMs(string value) => DateTimeOffset.Parse(value).ToUnixTimeMilliseconds();

    private static string TokenCountLine(string timestamp, int totalTokens) =>
        "{\"timestamp\":\"" + timestamp + "\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{\"last_token_usage\":{\"total_tokens\":" + totalTokens + "}}}}";

    private static string TokenUsageLine(string timestamp, int inputTokens, int cachedInputTokens, int outputTokens) =>
        "{\"timestamp\":\"" + timestamp + "\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{\"last_token_usage\":{\"input_tokens\":" + inputTokens + ",\"cached_input_tokens\":" + cachedInputTokens + ",\"output_tokens\":" + outputTokens + "}}}}";

    private static string ActivityLine(string timestamp) => EventLine(timestamp, "user_message");

    private static string EventLine(string timestamp, string payloadType) =>
        "{\"timestamp\":\"" + timestamp + "\",\"type\":\"event_msg\",\"payload\":{\"type\":\"" + payloadType + "\"}}";
}
