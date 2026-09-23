using System.Net;
using CodexHp.App.Infrastructure;
using Xunit;

namespace CodexHp.App.Tests.Infrastructure;

public sealed class ResetCreditsClientTests
{
    private const string Usage = """
        {"rate_limit":{"primary_window":{"used_percent":30,"reset_at":1791000000,"limit_window_seconds":604800}},
         "rate_limit_reset_credits":{"available_count":2,"applicable_available_count":0}}
        """;
    private const string Details = """
        {"available_count":2,"credits":[
          {"id":"one","status":"available","reset_type":"codex_rate_limits","expires_at":"2026-10-04T01:54:16.876788Z"},
          {"id":"two","status":"available","reset_type":"codex_rate_limits","expires_at":"2026-10-05T04:19:05Z"},
          {"id":"old","status":"consumed","reset_type":"codex_rate_limits","expires_at":"2026-09-01T00:00:00Z"}]}
        """;

    [Fact]
    public async Task Opted_in_request_reads_details_with_same_credentials_without_redeeming_anything()
    {
        var calls = new List<Uri>();
        using var handler = new Handler((request, _) =>
        {
            calls.Add(request.RequestUri!);
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("Bearer secret", request.Headers.Authorization!.ToString());
            Assert.Equal("account", request.Headers.GetValues("ChatGPT-Account-Id").Single());
            return Task.FromResult(Json(calls.Count == 1 ? Usage : Details));
        });
        var client = new OpenAiUsageClient(new HttpMessageInvoker(handler));
        var snapshot = await client.FetchAsync(new("secret", "account"), includeResetCredits: true);
        Assert.Equal(70, snapshot.WeeklyRemainingPercent);
        Assert.Equal(2, snapshot.ResetCredits!.AvailableCount);
        Assert.True(snapshot.ResetCredits.HasCompleteDetails);
        Assert.Equal(DateTimeOffset.Parse("2026-10-04T01:54:16.876788Z").ToUnixTimeMilliseconds(),
            snapshot.ResetCredits.Credits![0].ExpiresAtUnixMs);
        Assert.Equal(new[] { "/backend-api/wham/usage", "/backend-api/wham/rate-limit-reset-credits" },
            calls.Select(uri => uri.AbsolutePath));
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    public async Task Existing_mode_and_zero_inventory_do_not_fetch_details(bool enabled, int count)
    {
        var calls = 0;
        using var handler = new Handler((_, _) =>
        {
            calls++;
            return Task.FromResult(Json(Usage.Replace("\"available_count\":2", $"\"available_count\":{count}")));
        });
        var snapshot = await new OpenAiUsageClient(new HttpMessageInvoker(handler))
            .FetchAsync(new("secret", null), includeResetCredits: enabled);
        Assert.Equal(1, calls);
        Assert.Equal(count, snapshot.ResetCredits!.AvailableCount);
    }

    [Theory]
    [InlineData("http")]
    [InlineData("timeout")]
    [InlineData("malformed")]
    public async Task Detail_failure_preserves_fresh_usage_and_summary_count(string failure)
    {
        using var handler = new Handler((request, _) => request.RequestUri!.AbsolutePath.EndsWith("/usage", StringComparison.Ordinal)
            ? Task.FromResult(Json(Usage))
            : failure switch
            {
                "http" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)),
                "timeout" => Task.FromException<HttpResponseMessage>(new TaskCanceledException()),
                _ => Task.FromResult(Json("{ not-json }")),
            });
        var snapshot = await new OpenAiUsageClient(new HttpMessageInvoker(handler))
            .FetchAsync(new("secret", null), includeResetCredits: true);
        Assert.Equal(70, snapshot.WeeklyRemainingPercent);
        Assert.Equal(2, snapshot.ResetCredits!.AvailableCount);
        Assert.Null(snapshot.ResetCredits.Credits);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_swallowed_as_optional_detail_failure()
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new Handler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/usage", StringComparison.Ordinal))
                return Task.FromResult(Json(Usage));
            cancellation.Cancel();
            return Task.FromCanceled<HttpResponseMessage>(cancellation.Token);
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new OpenAiUsageClient(new HttpMessageInvoker(handler))
                .FetchAsync(new("secret", null), cancellation.Token, includeResetCredits: true));
    }

    [Fact]
    public void Unknown_expiries_and_capped_details_preserve_the_authoritative_count()
    {
        var snapshot = OpenAiUsageClient.ParseResetCreditsResponse("""
            {"available_count":3,"credits":[
              {"id":"one","status":"available","reset_type":"codex_rate_limits","expires_at":null},
              {"id":"two","status":"available","reset_type":"codex_rate_limits","expires_at":"invalid"}]}
            """);
        Assert.Equal(3, snapshot.AvailableCount);
        Assert.False(snapshot.HasCompleteDetails);
        Assert.All(snapshot.Credits!, credit => Assert.Null(credit.ExpiresAtUnixMs));
    }

    [Fact]
    public void Malformed_optional_summary_does_not_break_usage()
    {
        var snapshot = OpenAiUsageClient.ParseUsageResponse(Usage.Replace("\"available_count\":2", "\"available_count\":\"unknown\""));
        Assert.Equal(70, snapshot.WeeklyRemainingPercent);
        Assert.Null(snapshot.ResetCredits);
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
