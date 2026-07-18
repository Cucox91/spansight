using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Mvc.Testing;

using SpanSight.Api;

using Xunit;

namespace SpanSight.Api.Tests;

/// <summary>
/// FR-AI.1 endpoint behavior. No database needed — /api/ai/query never touches it. The enabled
/// path runs against the stub provider so the whole pipeline (schema shape → validation →
/// interpretation → DTO) is exercised without a key or spend (ADR-008).
/// </summary>
public class AiEndpointTests
{
    private static WebApplicationFactory<ApiAssemblyMarker> CreateFactory(params (string Key, string Value)[] settings)
    {
        return new WebApplicationFactory<ApiAssemblyMarker>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Otlp:Endpoint", "");
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        });
    }

    [Fact]
    public async Task Disabled_by_default_returns_503_problem()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/ai/query", new { text = "poor bridges" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("not enabled", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Enabled_without_a_provider_key_still_returns_503()
    {
        await using var factory = CreateFactory(("Ai:Enabled", "true"), ("Ai:Provider", "anthropic"));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/ai/query", new { text = "poor bridges" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Stub_provider_translates_to_the_rail_shaped_filter()
    {
        await using var factory = CreateFactory(("Ai:Enabled", "true"), ("Ai:Provider", "stub"));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/ai/query", new { text = "poor truss bridges in Florida built before 1970" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<NlQueryResponseDto>();
        Assert.NotNull(body);
        Assert.Equal("FL", body.Filter.State);
        Assert.Equal(["Poor"], body.Filter.Conditions);
        Assert.Equal(["Truss / Arch"], body.Filter.TypeGroups);
        Assert.Equal(1969, body.Filter.YearBuiltMax);
        Assert.Contains("Florida", body.Interpretation);
    }

    [Fact]
    public async Task Blank_text_returns_validation_problem()
    {
        await using var factory = CreateFactory(("Ai:Enabled", "true"), ("Ai:Provider", "stub"));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/ai/query", new { text = "  " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Budget_exhaustion_trips_to_temporarily_unavailable()
    {
        await using var factory = CreateFactory(
            ("Ai:Enabled", "true"), ("Ai:Provider", "stub"), ("Ai:MaxRequestsPerDay", "1"));
        using var client = factory.CreateClient();

        var first = await client.PostAsJsonAsync("/api/ai/query", new { text = "culverts" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Different text (a repeat would be served from cache without touching the budget).
        var second = await client.PostAsJsonAsync("/api/ai/query", new { text = "good bridges" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, second.StatusCode);
        Assert.Contains("temporarily unavailable", await second.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Repeated_query_is_served_from_cache_within_the_budget()
    {
        await using var factory = CreateFactory(
            ("Ai:Enabled", "true"), ("Ai:Provider", "stub"), ("Ai:MaxRequestsPerDay", "1"));
        using var client = factory.CreateClient();

        var first = await client.PostAsJsonAsync("/api/ai/query", new { text = "Culverts  in Florida" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Same normalized text — cache hit, no budget consumption, still 200.
        var second = await client.PostAsJsonAsync("/api/ai/query", new { text = "culverts in florida" });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }
}
