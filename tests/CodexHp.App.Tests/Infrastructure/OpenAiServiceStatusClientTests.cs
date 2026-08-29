using System.Net;
using CodexHp.App.Infrastructure;
using CodexHp.Core.Domain;
using Xunit;

namespace CodexHp.App.Tests.Infrastructure;

public sealed class OpenAiServiceStatusClientTests
{
    [Fact]
    public void ParseStatusResponse_maps_none_indicator_to_operational()
    {
        const string json = """
        {
          "page": { "updated_at": "2026-05-27T05:31:12Z" },
          "status": {
            "description": "All Systems Operational",
            "indicator": "none"
          }
        }
        """;

        var snapshot = OpenAiServiceStatusClient.ParseStatusResponse(json);

        Assert.Equal(ServiceHealthState.Operational, snapshot.Health);
        Assert.Equal("none", snapshot.Indicator);
        Assert.Equal("All Systems Operational", snapshot.Description);
        Assert.Equal(1779859872000, snapshot.UpdatedUnixMs);
    }

    [Fact]
    public void ParseStatusResponse_maps_non_none_indicator_to_issue()
    {
        const string json = """
        {
          "page": { "updated_at": "2026-05-27T05:31:12Z" },
          "status": {
            "description": "Partial System Degradation",
            "indicator": "minor"
          }
        }
        """;

        var snapshot = OpenAiServiceStatusClient.ParseStatusResponse(json);

        Assert.Equal(ServiceHealthState.Issue, snapshot.Health);
        Assert.Equal("minor", snapshot.Indicator);
    }

    [Fact]
    public void ParseStatusResponse_ignores_fedramp_only_component_issue()
    {
        const string json = """
        {
          "page": { "updated_at": "2026-05-27T05:31:12Z" },
          "status": {
            "description": "Partial System Degradation",
            "indicator": "minor"
          }
        }
        """;
        const string componentsJson = """
        {
          "components": [
            { "name": "CLI", "status": "operational" },
            { "name": "FedRAMP", "status": "degraded_performance" }
          ]
        }
        """;

        var snapshot = OpenAiServiceStatusClient.ParseStatusResponse(json, componentsJson);

        Assert.Equal(ServiceHealthState.Operational, snapshot.Health);
    }

    [Fact]
    public void ParseStatusResponse_lists_only_affected_non_fedramp_components()
    {
        const string json = """
        {
          "status": {
            "description": "Partial System Degradation",
            "indicator": "minor"
          }
        }
        """;
        const string componentsJson = """
        {
          "components": [
            { "name": "ChatGPT", "status": "degraded_performance" },
            { "name": "Codex", "status": "partial_outage" },
            { "name": "OpenAI API", "status": "operational" },
            { "name": "FedRAMP", "status": "degraded_performance" }
          ]
        }
        """;

        var snapshot = OpenAiServiceStatusClient.ParseStatusResponse(json, componentsJson);

        Assert.Equal(["ChatGPT", "Codex"], snapshot.AffectedComponents);
    }

    [Fact]
    public async Task FetchAsync_reads_components_only_when_global_status_is_issue()
    {
        using var handler = new CapturingHandler(request => JsonResponse(
            request.RequestUri?.AbsolutePath.EndsWith("/components.json", StringComparison.Ordinal) == true
                ? """
                  {
                    "components": [
                      { "name": "CLI", "status": "operational" },
                      { "name": "FedRAMP", "status": "degraded_performance" }
                    ]
                  }
                  """
                : """
                  {
                    "page": { "updated_at": "2026-05-27T05:31:12Z" },
                    "status": {
                      "description": "Partial System Degradation",
                      "indicator": "minor"
                    }
                  }
                  """));
        var client = new OpenAiServiceStatusClient(
            new HttpMessageInvoker(handler),
            new Uri("https://status.openai.com/api/v2/status.json"),
            new Uri("https://status.openai.com/api/v2/components.json"));

        var snapshot = await client.FetchAsync();

        Assert.Equal(ServiceHealthState.Operational, snapshot.Health);
        Assert.Equal(
            [
                "https://status.openai.com/api/v2/status.json",
                "https://status.openai.com/api/v2/components.json",
            ],
            handler.RequestUris);
        Assert.All(handler.Accept, value => Assert.Contains("application/json", value));
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json),
    };

    private sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<string> RequestUris { get; } = [];

        public List<string> Accept { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            this.RequestUris.Add(request.RequestUri?.AbsoluteUri ?? string.Empty);
            this.Accept.Add(request.Headers.Accept.ToString());
            return Task.FromResult(responder(request));
        }
    }
}
