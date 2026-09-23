using System.Net.Http;
using System.Text.Json;
using CodexHp.App.Application;

namespace CodexHp.App.Infrastructure;

internal sealed class GitHubUpdateClient(HttpMessageInvoker http, Version currentVersion)
{
    internal static readonly Uri LatestReleaseUri = new("https://api.github.com/repos/netics01/codexhp/releases/latest");

    internal async Task<AvailableUpdate?> FetchAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUri);
        request.Headers.UserAgent.ParseAdd("CodexHp");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
        using var response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        return Parse(json, currentVersion);
    }

    internal static AvailableUpdate? Parse(string json, Version currentVersion)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("draft", out var draft) || draft.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
            || !root.TryGetProperty("prerelease", out var prerelease) || prerelease.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new FormatException("Release classification is missing.");
        if (draft.GetBoolean() || prerelease.GetBoolean()) return null;
        if (!root.TryGetProperty("tag_name", out var tag) || tag.ValueKind != JsonValueKind.String)
            throw new FormatException("Release tag is missing.");
        var release = AvailableUpdate.FromTag(tag.GetString()!);
        var current = new Version(currentVersion.Major, currentVersion.Minor, Math.Max(0, currentVersion.Build));
        return release.Version > current ? release : null;
    }
}
