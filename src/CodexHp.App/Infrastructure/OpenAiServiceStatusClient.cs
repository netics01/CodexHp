using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CodexHp.App.Application;
using CodexHp.Core.Domain;

namespace CodexHp.App.Infrastructure;

public sealed record OpenAiServiceStatusSnapshot(
    ServiceHealthState Health,
    string Indicator,
    string Description,
    long UpdatedUnixMs,
    IReadOnlyList<string>? AffectedComponents = null,
    IReadOnlyList<string>? AffectedGroups = null,
    IReadOnlyList<ServiceStatusComponentGroup>? AffectedComponentGroups = null)
{
    public static OpenAiServiceStatusSnapshot Unknown(long updatedUnixMs) =>
        new(ServiceHealthState.Unknown, "unknown", string.Empty, updatedUnixMs, [], [], []);
}

public sealed class OpenAiServiceStatusClient : IOpenAiServiceStatusClient
{
    private static readonly Uri DefaultStatusUri = new("https://status.openai.com/api/v2/status.json");
    private static readonly Uri DefaultComponentsUri = new("https://status.openai.com/api/v2/components.json");
    private static readonly Uri DefaultStatusPageUri = new("https://status.openai.com/");
    private static readonly Regex AffectedGroupPattern = new(
        "<span\\b[^>]*>\\s*Affects\\s+(?<name>[^<]+?)\\s*</span>",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ScriptPattern = new(
        "<script\\b[^>]*>(?<body>[\\s\\S]*?)</script>",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private const string NextFlightPushPrefix = "self.__next_f.push(";
    private readonly HttpMessageInvoker http;
    private readonly Uri statusUri;
    private readonly Uri componentsUri;
    private readonly Uri statusPageUri;

    public OpenAiServiceStatusClient(
        HttpMessageInvoker http,
        Uri? statusUri = null,
        Uri? componentsUri = null,
        Uri? statusPageUri = null)
    {
        this.http = http ?? throw new ArgumentNullException(nameof(http));
        this.statusUri = statusUri ?? DefaultStatusUri;
        this.componentsUri = componentsUri ?? DefaultComponentsUri;
        this.statusPageUri = statusPageUri ?? DefaultStatusPageUri;
    }

    public async Task<OpenAiServiceStatusSnapshot> FetchAsync(CancellationToken cancellationToken = default)
    {
        var statusJson = await this.GetJsonAsync(this.statusUri, cancellationToken);
        var snapshot = ParseStatusResponse(statusJson);
        if (snapshot.Health != ServiceHealthState.Issue)
        {
            return snapshot;
        }

        var componentsTask = this.TryGetJsonAsync(this.componentsUri, cancellationToken);
        var statusPageTask = this.TryGetStatusPageHtmlAsync(cancellationToken);
        await Task.WhenAll(componentsTask, statusPageTask);

        var componentsJson = await componentsTask;
        if (componentsJson is not null)
        {
            try
            {
                snapshot = ParseStatusResponse(statusJson, componentsJson);
            }
            catch (JsonException)
            {
            }
        }

        if (snapshot.Health != ServiceHealthState.Issue)
        {
            return snapshot;
        }

        var statusPageHtml = await statusPageTask;
        var affectedComponentGroups = statusPageHtml is null
            ? []
            : ReadAffectedComponentGroups(statusPageHtml);
        var affectedGroups = affectedComponentGroups.Count > 0
            ? affectedComponentGroups.Select(group => group.Name).ToArray()
            : statusPageHtml is null
                ? []
                : ReadAffectedGroups(statusPageHtml);
        var affectedComponents = snapshot.AffectedComponents?.Count > 0
            ? snapshot.AffectedComponents
            : affectedComponentGroups.SelectMany(group => group.Components).ToArray();
        return snapshot with
        {
            AffectedComponents = affectedComponents,
            AffectedGroups = affectedGroups,
            AffectedComponentGroups = affectedComponentGroups,
        };
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

        return new OpenAiServiceStatusSnapshot(health, indicator, description, updatedUnixMs, affectedComponents, [], []);
    }

    private async Task<string> GetJsonAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await this.http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private async Task<string?> TryGetJsonAsync(Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            return await this.GetJsonAsync(uri, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private async Task<string> GetStatusPageHtmlAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, this.statusPageUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        using var response = await this.http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private async Task<string?> TryGetStatusPageHtmlAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await this.GetStatusPageHtmlAsync(cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
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

    private static IReadOnlyList<string> ReadAffectedGroups(string statusPageHtml)
    {
        ArgumentNullException.ThrowIfNull(statusPageHtml);

        var names = new List<string>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in AffectedGroupPattern.Matches(statusPageHtml))
        {
            var groupName = WebUtility.HtmlDecode(match.Groups["name"].Value).Trim();
            if (!string.IsNullOrWhiteSpace(groupName) && seenNames.Add(groupName))
            {
                names.Add(groupName);
            }
        }

        return names;
    }

    private static IReadOnlyList<ServiceStatusComponentGroup> ReadAffectedComponentGroups(
        string statusPageHtml)
    {
        ArgumentNullException.ThrowIfNull(statusPageHtml);

        foreach (Match script in ScriptPattern.Matches(statusPageHtml))
        {
            var body = script.Groups["body"].Value.Trim();
            if (!body.StartsWith(NextFlightPushPrefix, StringComparison.Ordinal)
                || !body.EndsWith(')'))
            {
                continue;
            }

            var argumentsJson = body[NextFlightPushPrefix.Length..^1];
            try
            {
                using var arguments = JsonDocument.Parse(argumentsJson);
                if (arguments.RootElement.ValueKind != JsonValueKind.Array
                    || arguments.RootElement.GetArrayLength() < 2
                    || arguments.RootElement[1].ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var payload = arguments.RootElement[1].GetString();
                if (string.IsNullOrWhiteSpace(payload)
                    || !TryParseFlightPayload(payload, out var payloadDocument))
                {
                    continue;
                }

                using (payloadDocument)
                {
                    if (TryReadAffectedComponentGroups(
                        payloadDocument.RootElement,
                        out var affectedGroups))
                    {
                        return affectedGroups;
                    }
                }
            }
            catch (JsonException)
            {
            }
        }

        return [];
    }

    private static bool TryParseFlightPayload(string payload, out JsonDocument document)
    {
        document = null!;
        var separatorIndex = payload.IndexOf(':');
        if (separatorIndex < 0 || separatorIndex == payload.Length - 1)
        {
            return false;
        }

        try
        {
            var json = Encoding.UTF8.GetBytes(payload[(separatorIndex + 1)..]);
            var reader = new Utf8JsonReader(json);
            document = JsonDocument.ParseValue(ref reader);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryReadAffectedComponentGroups(
        JsonElement element,
        out IReadOnlyList<ServiceStatusComponentGroup> affectedGroups)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("affected_components", out var affectedComponents)
            && element.TryGetProperty("structure", out var structure))
        {
            affectedGroups = CreateAffectedComponentGroups(affectedComponents, structure);
            if (affectedGroups.Count > 0)
            {
                return true;
            }
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (TryReadAffectedComponentGroups(property.Value, out affectedGroups))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (TryReadAffectedComponentGroups(item, out affectedGroups))
                {
                    return true;
                }
            }
        }

        affectedGroups = [];
        return false;
    }

    private static IReadOnlyList<ServiceStatusComponentGroup> CreateAffectedComponentGroups(
        JsonElement affectedComponents,
        JsonElement structure)
    {
        if (affectedComponents.ValueKind != JsonValueKind.Array
            || structure.ValueKind != JsonValueKind.Object
            || !structure.TryGetProperty("items", out var items)
            || items.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var affectedIds = affectedComponents
            .EnumerateArray()
            .Where(component => component.TryGetProperty("component_id", out var id)
                && id.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(id.GetString()))
            .Select(component => component.GetProperty("component_id").GetString()!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (affectedIds.Count == 0)
        {
            return [];
        }

        var groups = new List<ServiceStatusComponentGroup>();
        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("group", out var group)
                || group.ValueKind != JsonValueKind.Object
                || !group.TryGetProperty("name", out var groupNameElement)
                || string.IsNullOrWhiteSpace(groupNameElement.GetString())
                || !group.TryGetProperty("components", out var components)
                || components.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var groupName = groupNameElement.GetString()!.Trim();
            if (string.Equals(groupName, "FedRAMP", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var componentNames = new List<string>();
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var component in components.EnumerateArray())
            {
                if (!component.TryGetProperty("component_id", out var componentId)
                    || componentId.ValueKind != JsonValueKind.String
                    || !affectedIds.Contains(componentId.GetString()!)
                    || !component.TryGetProperty("name", out var componentNameElement)
                    || string.IsNullOrWhiteSpace(componentNameElement.GetString()))
                {
                    continue;
                }

                var componentName = componentNameElement.GetString()!.Trim();
                if (seenNames.Add(componentName))
                {
                    componentNames.Add(componentName);
                }
            }

            if (componentNames.Count > 0)
            {
                groups.Add(new ServiceStatusComponentGroup(groupName, componentNames));
            }
        }

        return groups;
    }
}
