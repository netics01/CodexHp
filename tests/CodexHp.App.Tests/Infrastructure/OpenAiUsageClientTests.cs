using System.Net;
using CodexHp.App.Infrastructure;
using Xunit;

namespace CodexHp.App.Tests.Infrastructure;

public sealed class OpenAiUsageClientTests
{
    [Fact]
    public async Task FetchAsync_sends_required_headers_and_maps_response()
    {
        using var handler = new CapturingHandler(_ => JsonResponse("""
        {
          "rate_limit": {
            "primary_window": {
              "used_percent": 62,
              "reset_at": 1780000000,
              "limit_window_seconds": 18000
            },
            "secondary_window": {
              "used_percent": 28,
              "reset_at": 1780500000,
              "limit_window_seconds": 604800
            }
          }
        }
        """));
        var client = new OpenAiUsageClient(
            new HttpMessageInvoker(handler),
            new Uri("https://chatgpt.com/backend-api/wham/usage"));

        var snapshot = await client.FetchAsync(new CodexCredentials("access-token", "account-123"));

        Assert.Equal(38, snapshot.SessionRemainingPercent);
        Assert.Equal(72, snapshot.WeeklyRemainingPercent);
        Assert.Equal(1780000000000, snapshot.SessionResetUnixMs);
        Assert.Equal(1780500000000, snapshot.WeeklyResetUnixMs);
        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal("https://chatgpt.com/backend-api/wham/usage", handler.RequestUri?.AbsoluteUri);
        Assert.Equal("Bearer access-token", handler.Authorization);
        Assert.Equal("CodexHp", handler.UserAgent);
        Assert.Contains("application/json", handler.Accept);
        Assert.Equal("account-123", handler.AccountId);
    }

    [Fact]
    public async Task FetchAsync_omits_account_header_when_account_id_is_missing()
    {
        using var handler = new CapturingHandler(_ => JsonResponse(WeeklyOnlyJson));
        var client = new OpenAiUsageClient(new HttpMessageInvoker(handler));

        await client.FetchAsync(new CodexCredentials("access-token", null));

        Assert.Null(handler.AccountId);
    }

    [Fact]
    public void ParseUsageResponse_normalizes_reversed_windows_and_clamps_percentages()
    {
        const string json = """
        {
          "rate_limit": {
            "primary_window": {
              "used_percent": -10,
              "reset_at": 1780500000,
              "limit_window_seconds": 604800
            },
            "secondary_window": {
              "used_percent": 120,
              "reset_at": 1780000000,
              "limit_window_seconds": 18000
            }
          }
        }
        """;

        var snapshot = OpenAiUsageClient.ParseUsageResponse(json);

        Assert.Equal(0, snapshot.SessionRemainingPercent);
        Assert.Equal(100, snapshot.WeeklyRemainingPercent);
        Assert.Equal(1780000000000, snapshot.SessionResetUnixMs);
        Assert.Equal(1780500000000, snapshot.WeeklyResetUnixMs);
    }

    [Fact]
    public void ParseUsageResponse_treats_missing_session_window_as_fully_available()
    {
        var snapshot = OpenAiUsageClient.ParseUsageResponse(WeeklyOnlyJson);

        Assert.Equal(100, snapshot.SessionRemainingPercent);
        Assert.Equal(long.MaxValue, snapshot.SessionResetUnixMs);
        Assert.Equal(18_000, snapshot.SessionWindowSeconds);
        Assert.Equal(72, snapshot.WeeklyRemainingPercent);
    }

    [Fact]
    public void ParseUsageResponse_rejects_missing_weekly_window_without_echoing_payload()
    {
        const string secret = "do-not-echo-this-value";
        var json = $$"""
        {
          "secret": "{{secret}}",
          "rate_limit": {
            "primary_window": {
              "used_percent": 10,
              "reset_at": 1780000000,
              "limit_window_seconds": 18000
            }
          }
        }
        """;

        var exception = Assert.Throws<UsageContractException>(() => OpenAiUsageClient.ParseUsageResponse(json));

        Assert.DoesNotContain(secret, exception.Message, StringComparison.Ordinal);
    }

    private const string WeeklyOnlyJson = """
    {
      "rate_limit": {
        "primary_window": {
          "used_percent": 28,
          "reset_at": 1780500000,
          "limit_window_seconds": 604800
        },
        "secondary_window": null
      }
    }
    """;

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json),
    };

    private sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }

        public Uri? RequestUri { get; private set; }

        public string? Authorization { get; private set; }

        public string? UserAgent { get; private set; }

        public string Accept { get; private set; } = string.Empty;

        public string? AccountId { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            this.Method = request.Method;
            this.RequestUri = request.RequestUri;
            this.Authorization = request.Headers.Authorization?.ToString();
            this.UserAgent = request.Headers.UserAgent.ToString();
            this.Accept = request.Headers.Accept.ToString();
            this.AccountId = request.Headers.TryGetValues("ChatGPT-Account-Id", out var values)
                ? values.Single()
                : null;
            return Task.FromResult(responder(request));
        }
    }
}
