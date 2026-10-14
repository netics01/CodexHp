using System.Text;
using CodexHp.App.Infrastructure;
using Xunit;

namespace CodexHp.App.Tests.Infrastructure;

public sealed class ClaudeDesktopUsageSourceTests : IDisposable
{
    private const string Org = "e1e6ea64-0000-4000-8000-000000000001";
    private const long Now = 1_792_000_000_000;
    private readonly string root = Path.Combine(Path.GetTempPath(), "CodexHp.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void A_newer_cached_response_supplies_percentages_and_reset_times()
    {
        this.WriteHistory((Now - 600_000, 40, 10));
        this.WriteCache((Now - 60_000, UsageJson(91, "2026-10-14T12:39:59.898177+00:00", 13.4, "2026-10-17T01:59:59+00:00")));

        var snapshot = this.Read();

        Assert.NotNull(snapshot);
        Assert.Equal(9, snapshot.SessionRemainingPercent);
        Assert.Equal(87, snapshot.WeeklyRemainingPercent);
        Assert.Equal(DateTimeOffset.Parse("2026-10-14T12:39:59.898+00:00").ToUnixTimeMilliseconds(), snapshot.SessionResetUnixMs);
        Assert.Equal(Now - 60_000, snapshot.ObservedUnixMs);
        Assert.Equal(ClaudeDesktopUsageSource.CacheSource, snapshot.Source);
        Assert.Equal(18_000, snapshot.SessionWindowSeconds);
        Assert.Equal(604_800, snapshot.WeeklyWindowSeconds);
    }

    [Fact]
    public void A_newer_history_sample_keeps_reset_times_that_have_not_passed()
    {
        var sessionReset = DateTimeOffset.FromUnixTimeMilliseconds(Now - 120_000);
        var weeklyReset = DateTimeOffset.FromUnixTimeMilliseconds(Now + 86_400_000);
        this.WriteHistory((Now - 60_000, 5, 14));
        this.WriteCache((Now - 3_600_000, UsageJson(91, sessionReset.ToString("O"), 13, weeklyReset.ToString("O"))));

        var snapshot = this.Read();

        Assert.NotNull(snapshot);
        Assert.Equal(95, snapshot.SessionRemainingPercent);
        Assert.Equal(86, snapshot.WeeklyRemainingPercent);
        Assert.Equal(0, snapshot.SessionResetUnixMs);
        Assert.Equal(weeklyReset.ToUnixTimeMilliseconds(), snapshot.WeeklyResetUnixMs);
        Assert.Equal(ClaudeDesktopUsageSource.HistorySource, snapshot.Source);
    }

    [Fact]
    public void History_alone_reports_percentages_without_reset_times()
    {
        this.WriteHistory((Now - 900_000, 12, 3), (Now - 300_000, 85, 12), (Now - 600_000, 50, 10));

        var snapshot = this.Read();

        Assert.NotNull(snapshot);
        Assert.Equal(15, snapshot.SessionRemainingPercent);
        Assert.Equal(88, snapshot.WeeklyRemainingPercent);
        Assert.Equal(0, snapshot.SessionResetUnixMs);
        Assert.Equal(0, snapshot.WeeklyResetUnixMs);
        Assert.Equal(Now - 300_000, snapshot.ObservedUnixMs);
    }

    [Fact]
    public void No_desktop_app_data_returns_null()
    {
        Assert.Null(this.Read());
    }

    [Fact]
    public void A_corrupt_cache_falls_back_to_history()
    {
        this.WriteHistory((Now - 60_000, 20, 30));
        var cache = Path.Combine(this.root, "Claude", "Cache", "Cache_Data");
        Directory.CreateDirectory(cache);
        File.WriteAllBytes(Path.Combine(cache, "index"), new byte[512]);

        var snapshot = this.Read();

        Assert.NotNull(snapshot);
        Assert.Equal(80, snapshot.SessionRemainingPercent);
    }

    [Theory]
    [InlineData("1/0/https://claude.ai/api/organizations/" + Org + "/usage", Org)]
    [InlineData("https://claude.ai/api/organizations/" + Org + "/usage", Org)]
    [InlineData("1/0/https://claude.ai/api/organizations/" + Org + "/usage/extra", null)]
    [InlineData("1/0/https://claude.ai/api/organizations/a/b/usage", null)]
    [InlineData("1/0/https://evil.test/?https://claude.ai/api/organizations/x/usage", null)]
    public void Parses_the_organization_from_usage_cache_keys(string key, string? expected)
    {
        Assert.Equal(expected, ClaudeDesktopUsageSource.ParseOrganization(key));
    }

    public void Dispose()
    {
        if (Directory.Exists(this.root))
        {
            Directory.Delete(this.root, recursive: true);
        }
    }

    private Core.Domain.UsageSnapshot? Read() =>
        new ClaudeDesktopUsageSource([Path.Combine(this.root, "Claude")]).Read(Now);

    private void WriteHistory(params (long T, int Fh, int Sd)[] samples)
    {
        var directory = Path.Combine(this.root, "Claude");
        Directory.CreateDirectory(directory);
        var json = "{\"version\":2,\"samples\":["
            + string.Join(",", samples.Select(s => $"{{\"t\":{s.T},\"org\":\"{Org}\",\"u\":{{\"fh\":{s.Fh},\"sd\":{s.Sd}}}}}"))
            + "]}";
        File.WriteAllText(Path.Combine(directory, "plan-usage-history.json"), json, Encoding.UTF8);
    }

    private void WriteCache(params (long ResponseUnixMs, string Json)[] responses)
    {
        var fixture = new ChromiumCacheFixture();
        foreach (var (time, json) in responses)
        {
            fixture.Add($"1/0/https://claude.ai/api/organizations/{Org}/usage", time,
                "HTTP/1.1 200\r\ncontent-type: application/json\r\ncontent-encoding: zstd", ChromiumCacheFixture.Zstd(json));
        }

        fixture.Add("1/0/https://claude.ai/api/organizations/other/usage", Now,
            "HTTP/1.1 200\r\ncontent-type: application/json", Encoding.UTF8.GetBytes(UsageJson(1, null, 1, null)));
        fixture.Write(Path.Combine(this.root, "Claude", "Cache", "Cache_Data"));
    }

    private static string UsageJson(double session, string? sessionReset, double weekly, string? weeklyReset) =>
        $"{{\"five_hour\":{{\"utilization\":{session},\"resets_at\":{Quote(sessionReset)}}},"
        + $"\"seven_day\":{{\"utilization\":{weekly},\"resets_at\":{Quote(weeklyReset)}}},\"limits\":[]}}";

    private static string Quote(string? value) => value is null ? "null" : $"\"{value}\"";
}
