using CodexHp.App.Infrastructure;
using CodexHp.Core.Domain;
using Xunit;

namespace CodexHp.App.Tests.Infrastructure;

public sealed class ClaudeTokenUsageScannerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "CodexHp.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Counts_new_input_cache_writes_and_output_but_not_cache_reads()
    {
        this.WriteLog("project/session.jsonl",
            Assistant("2026-10-14T12:00:05Z", "msg_1", "req_1", input: 2, cacheCreation: 100, cacheRead: 50_000, output: 30));

        var buckets = this.Scan("2026-10-14T12:00:10Z", maxBuckets: 2);

        Assert.Equal([0, 132], buckets);
    }

    [Fact]
    public void Counts_each_response_once_using_its_largest_logged_usage()
    {
        this.WriteLog("project/session.jsonl",
            Assistant("2026-10-14T12:00:01Z", "msg_1", "req_1", output: 5),
            Assistant("2026-10-14T12:00:02Z", "msg_1", "req_1", output: 40),
            Assistant("2026-10-14T12:00:03Z", "msg_2", "req_2", output: 7));

        var buckets = this.Scan("2026-10-14T12:00:10Z", maxBuckets: 1);

        Assert.Equal([47], buckets);
    }

    [Fact]
    public void Reads_subagent_logs_and_ignores_other_entries_and_old_responses()
    {
        this.WriteLog("project/session/subagents/agent.jsonl",
            Assistant("2026-10-14T12:00:20Z", "msg_3", "req_3", output: 9),
            "{\"type\":\"user\",\"timestamp\":\"2026-10-14T12:00:21Z\",\"message\":{\"usage\":{\"output_tokens\":999}}}",
            "{ invalid json \"usage\" \"assistant\"",
            Assistant("2026-10-14T11:00:00Z", "msg_old", "req_old", output: 500));

        var buckets = this.Scan("2026-10-14T12:00:30Z", maxBuckets: 2);

        Assert.Equal([9, 0], buckets);
    }

    [Fact]
    public void Missing_projects_directory_returns_empty_buckets()
    {
        Assert.Equal([0, 0, 0], this.Scan("2026-10-14T12:00:30Z", maxBuckets: 3));
    }

    [Fact]
    public void Desktop_app_detector_maps_package_families_to_providers()
    {
        var packages = new Dictionary<uint, string?>
        {
            [10] = "Claude_pzs8sxrjxfjjc",
            [11] = null,
            [20] = "OpenAI.Codex_2p2nqsd0c76g0",
        };
        var detector = new DesktopAppDetector(() => [11, 10], () => 20, id => packages.GetValueOrDefault(id));

        Assert.Equal(new DesktopAppState(true, UsageProvider.Codex), detector.Read());
        Assert.Equal(new DesktopAppState(false, null),
            new DesktopAppDetector(() => [11], () => 11, id => packages.GetValueOrDefault(id)).Read());
    }

    public void Dispose()
    {
        if (Directory.Exists(this.root))
        {
            Directory.Delete(this.root, recursive: true);
        }
    }

    private IReadOnlyList<int> Scan(string now, int maxBuckets) =>
        new ClaudeTokenUsageScanner(new Dictionary<string, string> { ["CLAUDE_CONFIG_DIR"] = this.root })
            .ReadRecentTokenBuckets(DateTimeOffset.Parse(now).ToUnixTimeMilliseconds(), 10, maxBuckets);

    private void WriteLog(string relativePath, params string[] lines)
    {
        var path = Path.Combine(this.root, "projects", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, lines);
    }

    private static string Assistant(
        string timestamp,
        string messageId,
        string requestId,
        long input = 0,
        long cacheCreation = 0,
        long cacheRead = 0,
        long output = 0) =>
        $"{{\"type\":\"assistant\",\"timestamp\":\"{timestamp}\",\"requestId\":\"{requestId}\","
        + $"\"message\":{{\"id\":\"{messageId}\",\"usage\":{{\"input_tokens\":{input},"
        + $"\"cache_creation_input_tokens\":{cacheCreation},\"cache_read_input_tokens\":{cacheRead},\"output_tokens\":{output}}}}}}}";
}
