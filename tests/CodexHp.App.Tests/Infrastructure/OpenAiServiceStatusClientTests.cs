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
    public async Task FetchAsync_reads_detail_sources_when_global_status_is_issue()
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
                "https://status.openai.com/",
            ],
            handler.RequestUris);
        Assert.Equal(["application/json", "application/json", "text/html"], handler.Accept);
    }

    [Fact]
    public async Task FetchAsync_prefers_the_status_pages_affected_group_label_over_an_underlying_component()
    {
        using var handler = new CapturingHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/api/v2/components.json" => JsonResponse("""
                {
                  "components": [
                    { "name": "Responses", "status": "degraded_performance" }
                  ]
                }
                """),
            "/" => JsonResponse("""
                <div>
                  <span>Affects APIs</span>
                </div>
                """),
            _ => JsonResponse("""
                {
                  "page": { "updated_at": "2026-05-27T05:31:12Z" },
                  "status": {
                    "description": "Partial System Degradation",
                    "indicator": "minor"
                  }
                }
                """),
        });
        var client = new OpenAiServiceStatusClient(
            new HttpMessageInvoker(handler),
            new Uri("https://status.openai.com/api/v2/status.json"),
            new Uri("https://status.openai.com/api/v2/components.json"));

        var snapshot = await client.FetchAsync();

        Assert.Equal(["Responses"], snapshot.AffectedComponents);
        Assert.Equal(["APIs"], GetAffectedGroups(snapshot));
        Assert.Equal(
            [
                "https://status.openai.com/api/v2/status.json",
                "https://status.openai.com/api/v2/components.json",
                "https://status.openai.com/",
            ],
            handler.RequestUris);
    }

    [Fact]
    public async Task FetchAsync_falls_back_to_underlying_components_when_the_status_page_cannot_be_read()
    {
        using var handler = new CapturingHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/api/v2/components.json" => JsonResponse("""
                {
                  "components": [
                    { "name": "Responses", "status": "degraded_performance" }
                  ]
                }
                """),
            "/" => throw new HttpRequestException("Status page is unavailable."),
            _ => JsonResponse("""
                {
                  "page": { "updated_at": "2026-05-27T05:31:12Z" },
                  "status": {
                    "description": "Partial System Degradation",
                    "indicator": "minor"
                  }
                }
                """),
        });
        var client = new OpenAiServiceStatusClient(
            new HttpMessageInvoker(handler),
            new Uri("https://status.openai.com/api/v2/status.json"),
            new Uri("https://status.openai.com/api/v2/components.json"));

        var snapshot = await client.FetchAsync();

        Assert.Equal(["Responses"], snapshot.AffectedComponents);
        Assert.Empty(GetAffectedGroups(snapshot));
        Assert.Equal(
            [
                "https://status.openai.com/api/v2/status.json",
                "https://status.openai.com/api/v2/components.json",
                "https://status.openai.com/",
            ],
            handler.RequestUris);
    }

    [Fact]
    public async Task FetchAsync_uses_status_page_groups_when_components_endpoint_fails()
    {
        using var handler = new CapturingHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/api/v2/components.json" => throw new HttpRequestException("Components are unavailable."),
            "/" => JsonResponse("<span>Affects APIs</span>"),
            _ => JsonResponse("""
                {
                  "status": {
                    "description": "Partial System Degradation",
                    "indicator": "minor"
                  }
                }
                """),
        });
        var client = new OpenAiServiceStatusClient(
            new HttpMessageInvoker(handler),
            new Uri("https://status.openai.com/api/v2/status.json"),
            new Uri("https://status.openai.com/api/v2/components.json"));

        var snapshot = await client.FetchAsync();

        Assert.Empty(snapshot.AffectedComponents ?? []);
        Assert.Equal(["APIs"], GetAffectedGroups(snapshot));
    }

    [Fact]
    public async Task FetchAsync_keeps_an_issue_without_details_when_both_detail_sources_fail()
    {
        using var handler = new CapturingHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/api/v2/components.json" => throw new HttpRequestException("Components are unavailable."),
            "/" => throw new HttpRequestException("Status page is unavailable."),
            _ => JsonResponse("""
                {
                  "status": {
                    "description": "Partial System Degradation",
                    "indicator": "minor"
                  }
                }
                """),
        });
        var client = new OpenAiServiceStatusClient(
            new HttpMessageInvoker(handler),
            new Uri("https://status.openai.com/api/v2/status.json"),
            new Uri("https://status.openai.com/api/v2/components.json"));

        var snapshot = await client.FetchAsync();

        Assert.Equal(ServiceHealthState.Issue, snapshot.Health);
        Assert.Empty(snapshot.AffectedComponents ?? []);
        Assert.Empty(GetAffectedGroups(snapshot));
    }

    [Fact]
    public async Task FetchAsync_keeps_multiple_groups_and_components_separate()
    {
        using var handler = new CapturingHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/api/v2/components.json" => JsonResponse("""
                {
                  "components": [
                    { "name": "Responses", "status": "degraded_performance" },
                    { "name": "Conversations", "status": "partial_outage" }
                  ]
                }
                """),
            "/" => JsonResponse("""
                <span>Affects APIs</span>
                <span>Affects ChatGPT</span>
                """),
            _ => JsonResponse("""
                {
                  "status": {
                    "description": "Partial System Degradation",
                    "indicator": "minor"
                  }
                }
                """),
        });
        var client = new OpenAiServiceStatusClient(
            new HttpMessageInvoker(handler),
            new Uri("https://status.openai.com/api/v2/status.json"),
            new Uri("https://status.openai.com/api/v2/components.json"));

        var snapshot = await client.FetchAsync();

        Assert.Equal(["Responses", "Conversations"], snapshot.AffectedComponents);
        Assert.Equal(["APIs", "ChatGPT"], GetAffectedGroups(snapshot));
    }

    private static IReadOnlyList<string> GetAffectedGroups(OpenAiServiceStatusSnapshot snapshot)
        => snapshot.AffectedGroups ?? [];

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
