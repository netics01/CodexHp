using System.Net;
using System.Net.Http;
using CodexHp.App.Application;
using CodexHp.App.Infrastructure;
using Xunit;

namespace CodexHp.App.Tests.Infrastructure;

public sealed class GitHubUpdateClientTests
{
    [Theory]
    [InlineData("v0.4.1", true)]
    [InlineData("0.10.0", true)]
    [InlineData("v1.0.0", true)]
    [InlineData("v0.4.0", false)]
    [InlineData("v0.4.0+different-commit", false)]
    [InlineData("v0.3.9", false)]
    public void Compares_numeric_versions_not_tag_strings_or_commit_hashes(string tag, bool newer)
    {
        var result = GitHubUpdateClient.Parse(Json(tag), new(0, 4, 0, 0));
        Assert.Equal(newer, result is not null);
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("v0.5")]
    [InlineData("v0.5.0-beta.1")]
    [InlineData("v0.5.0/../../evil")]
    [InlineData("https://example.org/")]
    [InlineData("v99999999999999.0.0")]
    [InlineData("v00.5.0")]
    public void Invalid_tags_are_failures_not_update_links(string tag) =>
        Assert.Throws<FormatException>(() => GitHubUpdateClient.Parse(Json(tag), new(0, 4, 0)));

    [Fact]
    public void Drafts_and_prereleases_are_never_offered()
    {
        Assert.Null(GitHubUpdateClient.Parse("""{"tag_name":"v9.0.0","draft":true,"prerelease":false}""", new(0, 4, 0)));
        Assert.Null(GitHubUpdateClient.Parse("""{"tag_name":"v9.0.0-beta","draft":false,"prerelease":true}""", new(0, 4, 0)));
        Assert.Throws<FormatException>(() => GitHubUpdateClient.Parse("""{"tag_name":"v9.0.0"}""", new(0, 4, 0)));
    }

    [Fact]
    public async Task Queries_the_public_endpoint_without_account_credentials_and_ignores_untrusted_urls()
    {
        using var handler = new Handler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(GitHubUpdateClient.LatestReleaseUri, request.RequestUri);
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("Cookie"));
            Assert.False(request.Headers.Contains("ChatGPT-Account-Id"));
            Assert.Contains("CodexHp", request.Headers.UserAgent.ToString());
            return new(HttpStatusCode.OK) { Content = new StringContent(Json("v0.5.0", "file:///untrusted.exe")) };
        });
        using var http = new HttpMessageInvoker(handler);
        var update = await new GitHubUpdateClient(http, new(0, 4, 0)).FetchAsync(default);
        Assert.Equal("https://github.com/netics01/codexhp/releases/tag/v0.5.0", update!.ReleaseUri.AbsoluteUri);
    }

    [Theory]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(429)]
    [InlineData(503)]
    public async Task Http_failures_are_not_reported_as_current(int status)
    {
        using var handler = new Handler(_ => new((HttpStatusCode)status));
        using var http = new HttpMessageInvoker(handler);
        await Assert.ThrowsAsync<HttpRequestException>(() => new GitHubUpdateClient(http, new(0, 4, 0)).FetchAsync(default));
    }

    private static string Json(string tag, string url = "https://example.org/untrusted") =>
        System.Text.Json.JsonSerializer.Serialize(new { tag_name = tag, draft = false, prerelease = false, html_url = url });

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
