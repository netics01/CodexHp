using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using CodexHp.App.Application;
using CodexHp.Core.Domain;

namespace CodexHp.App.Infrastructure;

public sealed record OpenAiServiceStatusSnapshot(
    ServiceHealthState Health,
    string Indicator,
    string Description,
    long UpdatedUnixMs,
    IReadOnlyList<string>? AffectedComponents = null)
{
    public static OpenAiServiceStatusSnapshot Unknown(long updatedUnixMs) =>
        new(ServiceHealthState.Unknown, "unknown", string.Empty, updatedUnixMs, []);
}

public sealed class OpenAiServiceStatusClient : IOpenAiServiceStatusClient
{
    private static readonly Uri DefaultStatusUri = new("https://status.openai.com/api/v2/status.json");
    private static readonly Uri DefaultComponentsUri = new("https://status.openai.com/api/v2/components.json");
    private readonly HttpMessageInvoker http;
    private readonly Uri statusUri;
    private readonly Uri componentsUri;

    public OpenAiServiceStatusClient(
        HttpMessageInvoker http,
        Uri? statusUri = null,
        Uri? componentsUri = null)
    {
        this.http = http ?? throw new ArgumentNullException(nameof(http));
        this.statusUri = statusUri ?? DefaultStatusUri;
        this.componentsUri = componentsUri ?? DefaultComponentsUri;
    }

    public async Task<OpenAiServiceStatusSnapshot> FetchAsync(CancellationToken cancellationToken = default)
    {
        var statusJson = await this.GetJsonAsync(this.statusUri, cancellationToken);
        var snapshot = ParseStatusResponse(statusJson);
        if (snapshot.Health != ServiceHealthState.Issue)
        {
            return snapshot;
        }

        var componentsJson = await this.GetJsonAsync(this.componentsUri, cancellationToken);
        return ParseStatusResponse(statusJson, componentsJson);
    }

    public static OpenAiServiceStatusSnapshot ParseStatusResponse(
        string json,
        string? componentsJson = null)
    {
        using var document = JsonDocument.Parse(json);
        using var componentsDocument = componentsJson is null ? null : JsonDocument.Parse(componentsJson);
        var root = document.RootElement;
        var componentsRoot = componentsDocument?.RootElement ?? root;
        var status = root.GetProperty("status");
        var indicator = status.GetProperty("indicator").GetString() ?? "unknown";
        var description = status.GetProperty("description").GetString() ?? string.Empty;
        var updatedUnixMs = ReadUpdatedUnixMs(root);
        var health = string.Equals(indicator, "none", StringComparison.OrdinalIgnoreCase)
            || HasOnlyFedRampComponentIssues(componentsRoot)
                ? ServiceHealthState.Operational
                : ServiceHealthState.Issue;
        var affectedComponents = health == ServiceHealthState.Issue
            ? ReadAffectedComponents(componentsRoot)
            : [];

        return new OpenAiServiceStatusSnapshot(health, indicator, description, updatedUnixMs, affectedComponents);
    }

    private async Task<string> GetJsonAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await this.http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static long ReadUpdatedUnixMs(JsonElement root)
    {
        if (root.TryGetProperty("page", out var page)
            && page.TryGetProperty("updated_at", out var updatedAt)
            && updatedAt.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(
                updatedAt.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var dateTime))
        {
            return dateTime.ToUnixTimeMilliseconds();
        }

        return 0;
    }

    private static bool HasOnlyFedRampComponentIssues(JsonElement root)
    {
        if (!root.TryGetProperty("components", out var components)
            || components.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var issueCount = 0;
        foreach (var component in components.EnumerateArray())
        {
            if (!component.TryGetProperty("status", out var status)
                || string.Equals(status.GetString(), "operational", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            issueCount++;
            if (!component.TryGetProperty("name", out var name)
                || !string.Equals(name.GetString(), "FedRAMP", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return issueCount > 0;
    }

    private static IReadOnlyList<string> ReadAffectedComponents(JsonElement root)
    {
        if (!root.TryGetProperty("components", out var components)
            || components.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var names = new List<string>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var component in components.EnumerateArray())
        {
            if (!component.TryGetProperty("status", out var status)
                || string.Equals(status.GetString(), "operational", StringComparison.OrdinalIgnoreCase)
                || !component.TryGetProperty("name", out var name)
                || string.IsNullOrWhiteSpace(name.GetString()))
            {
                continue;
            }

            var componentName = name.GetString()!.Trim();
            if (string.Equals(componentName, "FedRAMP", StringComparison.OrdinalIgnoreCase)
                || !seenNames.Add(componentName))
            {
                continue;
            }

            names.Add(componentName);
        }

        return names;
    }
}
