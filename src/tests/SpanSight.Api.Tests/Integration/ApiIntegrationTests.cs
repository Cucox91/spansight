using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SpanSight.Api.Tests.Integration;

[Collection("postgis-api")]
public class ApiIntegrationTests(PostgisApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private async Task<JsonElement> GetJsonAsync(string url)
    {
        var response = await fixture.Client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    [DockerFact]
    public async Task Seed_loaded_expected_fixture_split()
    {
        Assert.Equal(114, fixture.SeedSummary.RowsRead);
        Assert.Equal(99, fixture.SeedSummary.RowsLoaded);
        Assert.Equal(15, fixture.SeedSummary.RowsQuarantined);
        Assert.Equal(99, await fixture.CountBridgesAsync());
    }

    [DockerFact]
    public async Task Reload_of_same_file_is_a_noop_and_force_converges()
    {
        var again = await fixture.LoadFixtureAsync(force: false);
        Assert.True(again.Skipped);

        var forced = await fixture.LoadFixtureAsync(force: true);
        Assert.False(forced.Skipped);
        Assert.Equal(99, forced.RowsLoaded);
        Assert.Equal(99, await fixture.CountBridgesAsync()); // upsert converged, no duplicates
    }

    [DockerFact]
    public async Task State_filter_returns_only_that_state()
    {
        var all = await GetJsonAsync("/api/bridges?pageSize=200");
        var florida = await GetJsonAsync("/api/bridges?state=FL&pageSize=200");

        var totalAll = all.GetProperty("totalCount").GetInt32();
        var totalFl = florida.GetProperty("totalCount").GetInt32();

        Assert.True(totalFl > 0);
        Assert.True(totalFl < totalAll);
        foreach (var item in florida.GetProperty("items").EnumerateArray())
        {
            Assert.Equal("FL", item.GetProperty("state").GetString());
        }
    }

    [DockerFact]
    public async Task Filters_combine_and_match_manual_count()
    {
        var poorSteel = await GetJsonAsync("/api/bridges?state=FL&condition=Poor&material=3&pageSize=200");

        foreach (var item in poorSteel.GetProperty("items").EnumerateArray())
        {
            Assert.Equal("Poor", item.GetProperty("conditionClass").GetString());
            Assert.Equal("3", item.GetProperty("materialCode").GetString());
        }
    }

    [DockerFact]
    public async Task Bbox_filter_uses_postgis_and_stays_inside_the_box()
    {
        // Generous box around Miami-Dade fixture anchors.
        var box = await GetJsonAsync("/api/bridges?bbox=-80.6,25.4,-79.9,26.1&pageSize=200");

        var items = box.GetProperty("items").EnumerateArray().ToList();
        Assert.NotEmpty(items);
        foreach (var item in items)
        {
            var lat = item.GetProperty("latitude").GetDouble();
            var lon = item.GetProperty("longitude").GetDouble();
            Assert.InRange(lat, 25.4, 26.1);
            Assert.InRange(lon, -80.6, -79.9);
        }
    }

    [DockerFact]
    public async Task Pagination_pages_are_disjoint_and_sized()
    {
        var page1 = await GetJsonAsync("/api/bridges?pageSize=10&page=1");
        var page2 = await GetJsonAsync("/api/bridges?pageSize=10&page=2");

        var ids1 = page1.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()).ToHashSet();
        var ids2 = page2.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()).ToHashSet();

        Assert.Equal(10, ids1.Count);
        Assert.Empty(ids1.Intersect(ids2));
    }

    [DockerFact]
    public async Task Oversized_page_and_bad_state_return_problem_details()
    {
        var tooBig = await fixture.Client.GetAsync("/api/bridges?pageSize=500");
        Assert.Equal(HttpStatusCode.BadRequest, tooBig.StatusCode);
        var body = await tooBig.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.True(body.GetProperty("errors").TryGetProperty("pageSize", out _));

        var badState = await fixture.Client.GetAsync("/api/bridges?state=ZZ");
        Assert.Equal(HttpStatusCode.BadRequest, badState.StatusCode);
    }

    [DockerFact]
    public async Task Detail_decodes_codes_and_unknown_returns_404()
    {
        var list = await GetJsonAsync("/api/bridges?state=FL&pageSize=1");
        var id = list.GetProperty("items")[0].GetProperty("id").GetString()!;
        var structureNumber = id["FL-".Length..];

        var detail = await GetJsonAsync($"/api/bridges/FL/{Uri.EscapeDataString(structureNumber)}");
        Assert.Equal("Florida", detail.GetProperty("stateName").GetString());
        Assert.False(string.IsNullOrEmpty(detail.GetProperty("material").GetString()));
        Assert.False(string.IsNullOrEmpty(detail.GetProperty("deck").GetProperty("text").GetString()));

        var missing = await fixture.Client.GetAsync("/api/bridges/FL/DOESNOTEXIST");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [DockerFact]
    public async Task GeoJson_reports_totals_consistent_with_list()
    {
        var list = await GetJsonAsync("/api/bridges?state=TX&pageSize=200");
        var geo = await GetJsonAsync("/api/bridges/geojson?state=TX");

        Assert.Equal("FeatureCollection", geo.GetProperty("type").GetString());
        Assert.Equal(
            list.GetProperty("totalCount").GetInt32(),
            geo.GetProperty("meta").GetProperty("total").GetInt32());
        Assert.False(geo.GetProperty("meta").GetProperty("truncated").GetBoolean());
    }

    [DockerFact]
    public async Task Stats_reconcile_with_list_and_qa_reconciles_with_run_summary()
    {
        var list = await GetJsonAsync("/api/bridges?pageSize=1");
        var stats = await GetJsonAsync("/api/stats/summary");
        var qa = await GetJsonAsync("/api/qa/summary");

        var total = stats.GetProperty("total").GetInt32();
        Assert.Equal(list.GetProperty("totalCount").GetInt32(), total);

        var byCondition = stats.GetProperty("byCondition").EnumerateObject().Sum(p => p.Value.GetInt32());
        Assert.Equal(total, byCondition);

        var run = qa.GetProperty("latestRun");
        Assert.Equal(114, run.GetProperty("rowsRead").GetInt32());
        Assert.Equal(total, run.GetProperty("rowsLoaded").GetInt32()); // FR-0.2 AC-3: QA reconciles with core

        var reasons = qa.GetProperty("byReason").EnumerateArray()
            .Select(r => r.GetProperty("reason").GetString())
            .ToHashSet();
        Assert.Contains("coordinate_outside_state", reasons);
        Assert.Contains("duplicate_key", reasons);
        Assert.Contains("row_structural_fault", reasons);
    }

    [DockerFact]
    public async Task Lookups_serve_code_tables()
    {
        var lookups = await GetJsonAsync("/api/lookups");

        Assert.Contains(lookups.GetProperty("states").EnumerateArray(),
            s => s.GetProperty("abbreviation").GetString() == "FL");
        Assert.Equal("Steel", lookups.GetProperty("materials").GetProperty("3").GetString());
    }

    [DockerFact]
    public async Task Ai_endpoint_is_dark_by_default()
    {
        var response = await fixture.Client.PostAsJsonAsync(
            "/api/ai/query", new { text = "poor bridges near Miami" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [DockerFact]
    public async Task Health_endpoints_answer()
    {
        var live = await fixture.Client.GetAsync("/healthz");
        var ready = await fixture.Client.GetAsync("/readyz");

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
    }
}
