using System.Net.Http.Headers;
using System.Net.Http;
using System.Text.Json;
using CodexHp.App.Application;
using CodexHp.Core.Domain;

namespace CodexHp.App.Infrastructure;

public sealed class UsageContractException : Exception
{
    public UsageContractException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed class OpenAiUsageClient : IOpenAiUsageClient
{
    private const int SessionWindowSeconds = 18_000;
    private const int WeeklyWindowSeconds = 604_800;
    private static readonly Uri DefaultUsageUri = new("https://chatgpt.com/backend-api/wham/usage");
    private readonly HttpMessageInvoker http;
    private readonly Uri usageUri;

    public OpenAiUsageClient(HttpMessageInvoker http, Uri? usageUri = null)
    {
        this.http = http ?? throw new ArgumentNullException(nameof(http));
        this.usageUri = usageUri ?? DefaultUsageUri;
    }

    public async Task<UsageSnapshot> FetchAsync(
        CodexCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        if (string.IsNullOrWhiteSpace(credentials.AccessToken))
        {
            throw new ArgumentException("Access token is required.", nameof(credentials));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, this.usageUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
        request.Headers.UserAgent.ParseAdd("CodexHp");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrWhiteSpace(credentials.AccountId))
        {
            request.Headers.TryAddWithoutValidation("ChatGPT-Account-Id", credentials.AccountId);
        }

        using var response = await this.http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseUsageResponse(json);
    }

    public static UsageSnapshot ParseUsageResponse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var rateLimit = document.RootElement.GetProperty("rate_limit");
            var windows = new[]
            {
                ReadWindow(rateLimit, "primary_window"),
                ReadWindow(rateLimit, "secondary_window"),
            }.Where(window => window is not null).Cast<UsageWindow>().ToArray();
            var session = windows.FirstOrDefault(window => window.LimitWindowSeconds == SessionWindowSeconds);
            var weekly = windows.FirstOrDefault(window => window.LimitWindowSeconds == WeeklyWindowSeconds)
                ?? throw new UsageContractException("Weekly usage window is missing.");

            return new UsageSnapshot(
                SessionRemainingPercent: session is null ? 100 : RemainingPercent(session.UsedPercent),
                WeeklyRemainingPercent: RemainingPercent(weekly.UsedPercent),
                SessionResetUnixMs: session is null ? long.MaxValue : checked(session.ResetAtUnixSeconds * 1000),
                SessionWindowSeconds: SessionWindowSeconds,
                WeeklyResetUnixMs: checked(weekly.ResetAtUnixSeconds * 1000),
                WeeklyWindowSeconds: WeeklyWindowSeconds);
        }
        catch (UsageContractException)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or OverflowException)
        {
            throw new UsageContractException("Usage response has an unsupported format.", ex);
        }
    }

    private static UsageWindow? ReadWindow(JsonElement rateLimit, string key)
    {
        if (!rateLimit.TryGetProperty(key, out var window) || window.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return new UsageWindow(
            UsedPercent: window.GetProperty("used_percent").GetInt32(),
            ResetAtUnixSeconds: window.GetProperty("reset_at").GetInt64(),
            LimitWindowSeconds: window.GetProperty("limit_window_seconds").GetInt32());
    }

    private static int RemainingPercent(int usedPercent) => Math.Clamp(100 - usedPercent, 0, 100);

    private sealed record UsageWindow(int UsedPercent, long ResetAtUnixSeconds, int LimitWindowSeconds);
}
