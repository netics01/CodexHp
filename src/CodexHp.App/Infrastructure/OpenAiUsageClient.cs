using System.Net.Http.Headers;
using System.Net.Http;
using System.Text.Json;
using System.Globalization;
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
    private readonly Uri resetCreditsUri;

    public OpenAiUsageClient(HttpMessageInvoker http, Uri? usageUri = null, Uri? resetCreditsUri = null)
    {
        this.http = http ?? throw new ArgumentNullException(nameof(http));
        this.usageUri = usageUri ?? DefaultUsageUri;
        this.resetCreditsUri = resetCreditsUri ?? new Uri(this.usageUri, "rate-limit-reset-credits");
    }

    public async Task<UsageSnapshot> FetchAsync(
        CodexCredentials credentials,
        CancellationToken cancellationToken = default,
        bool includeResetCredits = false)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        if (string.IsNullOrWhiteSpace(credentials.AccessToken))
        {
            throw new ArgumentException("Access token is required.", nameof(credentials));
        }

        var json = await this.GetJsonAsync(this.usageUri, credentials, cancellationToken);
        var snapshot = ParseUsageResponse(json);
        if (!includeResetCredits || snapshot.ResetCredits?.AvailableCount == 0)
        {
            return snapshot;
        }

        // Optional enrichment must never turn valid usage into "Usage unavailable".
        // Do not reuse old detail rows with a fresh inventory count after a failed request.
        using var detailTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        detailTimeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var details = await this.GetJsonAsync(this.resetCreditsUri, credentials, detailTimeout.Token);
            return snapshot with { ResetCredits = ParseResetCreditsResponse(details) };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or UsageContractException)
        {
            return snapshot;
        }
    }

    private async Task<string> GetJsonAsync(Uri uri, CodexCredentials credentials, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
        request.Headers.UserAgent.ParseAdd("CodexHp");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrWhiteSpace(credentials.AccountId))
        {
            request.Headers.TryAddWithoutValidation("ChatGPT-Account-Id", credentials.AccountId);
        }

        using var response = await this.http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
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
                WeeklyWindowSeconds: WeeklyWindowSeconds)
            {
                ResetCredits = ReadResetCreditsSummary(document.RootElement),
            };
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

    private static ResetCreditsSnapshot? ReadResetCreditsSummary(JsonElement root)
    {
        if (root.TryGetProperty("rate_limit_reset_credits", out var summary)
            && TryReadCount(summary) is { } count)
        {
            return new ResetCreditsSnapshot(count, count == 0 ? [] : null);
        }
        return null;
    }

    private static int? TryReadCount(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("available_count", out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var count) && count >= 0 ? count : null;

    public static ResetCreditsSnapshot ParseResetCreditsResponse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var count = TryReadCount(root)
                ?? throw new UsageContractException("Reset credit count is missing.");
            if (!root.TryGetProperty("credits", out var rows) || rows.ValueKind != JsonValueKind.Array)
            {
                return new ResetCreditsSnapshot(count);
            }

            var credits = new List<ResetCredit>();
            foreach (var row in rows.EnumerateArray())
            {
                if (ReadString(row, "status") != "available"
                    || ReadString(row, "reset_type") != "codex_rate_limits"
                    || ReadString(row, "id") is not { Length: > 0 } id)
                {
                    continue;
                }

                var expiry = DateTimeOffset.TryParse(
                    ReadString(row, "expires_at"), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var parsed)
                        && parsed.ToUnixTimeMilliseconds() > 0
                    ? parsed.ToUnixTimeMilliseconds() : (long?)null;
                credits.Add(new ResetCredit(id, expiry));
            }
            return new ResetCreditsSnapshot(count, credits);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or OverflowException)
        {
            throw new UsageContractException("Reset credit response has an unsupported format.", exception);
        }
    }

    private static string? ReadString(JsonElement row, string name) =>
        row.ValueKind == JsonValueKind.Object && row.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

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
