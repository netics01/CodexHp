using CodexHp.App.Infrastructure;
using Xunit;

namespace CodexHp.App.Tests.Infrastructure;

public sealed class TokenFileCursorCacheTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "CodexHp.Tests",
        Guid.NewGuid().ToString("N"));

    public TokenFileCursorCacheTests()
    {
        Directory.CreateDirectory(this.directory);
    }

    [Fact]
    public void ReadLines_reuses_snapshot_when_file_is_unchanged()
    {
        var file = this.FilePath();
        File.WriteAllText(file, "one" + Environment.NewLine);
        var cache = new TokenFileCursorCache();

        var first = cache.ReadLines(file);
        var second = cache.ReadLines(file);

        Assert.Equal(["one"], first);
        Assert.Same(first, second);
    }

    [Fact]
    public void ReadLines_appends_only_new_complete_lines()
    {
        var file = this.FilePath();
        File.WriteAllText(file, "one" + Environment.NewLine);
        var cache = new TokenFileCursorCache();
        cache.ReadLines(file);

        File.AppendAllText(file, "two" + Environment.NewLine);
        var lines = cache.ReadLines(file);

        Assert.Equal(["one", "two"], lines);
    }

    [Fact]
    public void ReadLines_appends_when_length_grows_without_a_last_write_time_change()
    {
        var file = this.FilePath();
        var firstLine = $"first-{Guid.NewGuid():N}";
        var staleLastWriteTime = DateTime.UtcNow.AddHours(-1);
        File.WriteAllText(file, firstLine + Environment.NewLine);
        File.SetLastWriteTimeUtc(file, staleLastWriteTime);
        var cache = new TokenFileCursorCache();
        var first = cache.ReadLines(file);

        File.AppendAllText(file, "second" + Environment.NewLine);
        File.SetLastWriteTimeUtc(file, staleLastWriteTime);
        var second = cache.ReadLines(file);

        Assert.Equal([firstLine, "second"], second);
        Assert.Same(first[0], second[0]);
    }

    [Fact]
    public void ReadLines_replaces_a_previous_unterminated_tail_when_it_is_completed()
    {
        var file = this.FilePath();
        File.WriteAllText(file, "one");
        var cache = new TokenFileCursorCache();

        Assert.Equal(["one"], cache.ReadLines(file));
        File.AppendAllText(file, Environment.NewLine + "two");

        Assert.Equal(["one", "two"], cache.ReadLines(file));
    }

    [Fact]
    public void ReadLines_restarts_after_file_is_truncated()
    {
        var file = this.FilePath();
        File.WriteAllText(file, "first-long-line" + Environment.NewLine + "second" + Environment.NewLine);
        var cache = new TokenFileCursorCache();
        cache.ReadLines(file);

        File.WriteAllText(file, "replacement" + Environment.NewLine);
        var lines = cache.ReadLines(file);

        Assert.Equal(["replacement"], lines);
    }

    [Fact]
    public void Instance_scanner_replays_cached_lines_once_after_append()
    {
        var sessionDirectory = Path.Combine(this.directory, "sessions", "2026", "05", "25");
        Directory.CreateDirectory(sessionDirectory);
        var file = Path.Combine(sessionDirectory, "active.jsonl");
        File.WriteAllText(file, TokenCountLine("2026-05-25T15:17:55.000Z", 11) + Environment.NewLine);
        var scanner = new CodexTokenUsageScanner(
            new Dictionary<string, string> { ["CODEX_HOME"] = this.directory },
            new TokenFileCursorCache());
        var now = DateTimeOffset.Parse("2026-05-25T15:18:00.000Z").ToUnixTimeMilliseconds();

        Assert.Equal([0, 0, 11], scanner.ReadRecentTokenBuckets(now, 10, 3));
        File.AppendAllText(file, TokenCountLine("2026-05-25T15:17:56.000Z", 7) + Environment.NewLine);

        Assert.Equal([0, 0, 18], scanner.ReadRecentTokenBuckets(now, 10, 3));
    }

    public void Dispose()
    {
        if (Directory.Exists(this.directory))
        {
            Directory.Delete(this.directory, recursive: true);
        }
    }

    private string FilePath() => Path.Combine(this.directory, "active.jsonl");

    private static string TokenCountLine(string timestamp, int totalTokens) =>
        "{\"timestamp\":\"" + timestamp + "\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{\"last_token_usage\":{\"total_tokens\":" + totalTokens + "}}}}";
}
