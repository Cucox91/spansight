using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using SpanSight.Api.Endpoints;
using SpanSight.Core.Domain;

namespace SpanSight.Api.Tests.Integration;

/// <summary>
/// FR-1.4 AC-1/AC-2/AC-3 against real PostGIS: rankings, the county report card, and the CSV
/// exports of both, over the committed 114-row fixture loaded by the production pipeline.
/// <para>
/// The byte-for-byte shape of an export is pinned separately in <c>CsvExportGoldenTests</c> against
/// hand-built DTOs. What is asserted here is what only a database can show: that the numbers in the
/// CSV are the numbers in the JSON, that the rules the API states are the rules it actually applied,
/// and that the counts reconcile with the tables they came from.
/// </para>
/// </summary>
[Collection("postgis-api")]
public class RankingIntegrationTests(PostgisApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private async Task<JsonElement> GetJsonAsync(string url)
    {
        var response = await fixture.Client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    private async Task<(string Body, HttpResponseMessage Response)> GetCsvAsync(string url)
    {
        var response = await fixture.Client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadAsStringAsync(), response);
    }

    /// <summary>The data rows of an export — everything after the leading comment block.</summary>
    private static List<string> DataRows(string csv) =>
        [.. csv.Split('\n').Where(l => l.Length > 0 && l[0] != CsvExportPrefix)];

    private const char CsvExportPrefix = '#';

    // ---------------------------------------------------------------- AC-1 definitions

    /// <summary>
    /// AC-1's whole requirement: the sort and inclusion definition is served with the rows, so no
    /// view can render the ordering without it.
    /// </summary>
    [DockerTheory]
    [InlineData("?groupBy=county")]
    [InlineData("?groupBy=state")]
    [InlineData("?groupBy=cohort")]
    [InlineData("?view=high-adt-poor")]
    public async Task Every_ranking_serves_the_rule_that_produced_it(string query)
    {
        var ranking = await GetJsonAsync($"/api/rankings{query}");
        var definition = ranking.GetProperty("definition");

        foreach (var field in (ReadOnlySpan<string>)["headline", "sortedBy", "includes", "excludes", "note"])
        {
            Assert.False(
                string.IsNullOrWhiteSpace(definition.GetProperty(field).GetString()),
                $"definition.{field} is empty for {query} — a ranking without its rule is unreadable.");
        }

        // GR-6: the note denies what the list is not, in the payload rather than only in the SPA.
        Assert.Contains("not a priority list", definition.GetProperty("note").GetString());
        Assert.Contains("record type 1", definition.GetProperty("includes").GetString());
    }

    /// <summary>
    /// A share-based ranking states how much its own floor removed. Without this the list looks
    /// exhaustive, which is the failure mode a minimum-size rule creates while fixing another.
    /// </summary>
    [DockerFact]
    public async Task A_share_ranking_publishes_what_the_minimum_group_size_set_aside()
    {
        var ranking = await GetJsonAsync("/api/rankings?groupBy=county");
        var definition = ranking.GetProperty("definition");

        Assert.Equal(
            RankingEndpoints.MinimumGroupSize, definition.GetProperty("minimumGroupSize").GetInt32());

        // The fixture is 114 bridges across a handful of counties, so every county is below the
        // 50-structure floor and the ranking is empty — which is the correct answer, and it is
        // accompanied by the count of what was excluded rather than by silence.
        Assert.Empty(ranking.GetProperty("groups").EnumerateArray());
        Assert.True(definition.GetProperty("excludedGroups").GetInt32() > 0);
        Assert.True(definition.GetProperty("excludedStructures").GetInt64() > 0);
        Assert.Contains("minimum", definition.GetProperty("excludes").GetString());
    }

    /// <summary>
    /// The excluded counts must be the real remainder, not a constant. Summing what the ranking
    /// omitted and what it kept has to account for every record-type-1 structure with a county code.
    /// </summary>
    [DockerFact]
    public async Task The_excluded_counts_reconcile_with_the_table_they_came_from()
    {
        var ranking = await GetJsonAsync("/api/rankings?groupBy=county&limit=500");
        var definition = ranking.GetProperty("definition");

        var ranked = ranking.GetProperty("groups").EnumerateArray()
            .Sum(g => (long)g.GetProperty("structures").GetInt32());
        var excluded = definition.GetProperty("excludedStructures").GetInt64();

        await using var db = fixture.NewDbContext();
        var eligible = await db.Bridges.CountAsync(b => b.RecordType == "1" && b.CountyCode != null);

        Assert.Equal(eligible, ranked + excluded);
    }

    /// <summary>
    /// The structure-level list has no share, so it must publish neither a denominator nor a floor —
    /// and it says how many Poor structures it could not place for want of a traffic count.
    /// </summary>
    [DockerFact]
    public async Task The_structure_list_states_what_it_could_not_order()
    {
        var ranking = await GetJsonAsync("/api/rankings?view=high-adt-poor");
        var definition = ranking.GetProperty("definition");

        Assert.Equal(JsonValueKind.Null, definition.GetProperty("denominator").ValueKind);
        Assert.Equal(JsonValueKind.Null, definition.GetProperty("minimumGroupSize").ValueKind);
        Assert.Empty(ranking.GetProperty("groups").EnumerateArray());

        await using var db = fixture.NewDbContext();
        var poorWithoutAdt = await db.Bridges.CountAsync(
            b => b.RecordType == "1" && b.ConditionClass == ConditionClass.Poor && b.Adt == null);

        Assert.Equal(poorWithoutAdt, definition.GetProperty("excludedStructures").GetInt64());
    }

    /// <summary>
    /// Every listed structure really is Poor and really has a traffic count, in descending order.
    /// The definition claims all three; this is the assertion that the claim is true.
    /// </summary>
    [DockerFact]
    public async Task The_structure_list_is_poor_condition_ordered_by_published_traffic()
    {
        var ranking = await GetJsonAsync("/api/rankings?view=high-adt-poor&limit=100");
        var rows = ranking.GetProperty("structures").EnumerateArray().ToList();

        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Equal("Poor", r.GetProperty("conditionClass").GetString()));

        var adt = rows.Select(r => r.GetProperty("adt").GetInt32()).ToList();
        Assert.Equal(adt.OrderByDescending(a => a).ToList(), adt);

        var ranks = rows.Select(r => r.GetProperty("rank").GetInt32()).ToList();
        Assert.Equal(Enumerable.Range(1, rows.Count).ToList(), ranks);
    }

    /// <summary>
    /// Rankings count record type 1 only. The map's totals do not, which is the discrepancy the
    /// definition text exists to explain — so the two must genuinely differ here, or the
    /// explanation is describing something that is not happening.
    /// </summary>
    [DockerFact]
    public async Task A_ranking_counts_structures_where_the_stats_summary_counts_every_served_row()
    {
        await using var db = fixture.NewDbContext();
        var served = await db.Bridges.CountAsync();
        var structures = await db.Bridges.CountAsync(b => b.RecordType == "1");

        var stats = await GetJsonAsync("/api/stats/summary");
        Assert.Equal(served, stats.GetProperty("total").GetInt32());

        var ranking = await GetJsonAsync("/api/rankings?groupBy=state&limit=500");
        var ranked = ranking.GetProperty("groups").EnumerateArray()
            .Sum(g => (long)g.GetProperty("structures").GetInt32())
            + ranking.GetProperty("definition").GetProperty("excludedStructures").GetInt64();

        Assert.Equal(structures, ranked);
    }

    [DockerFact]
    public async Task An_unknown_view_is_a_validation_problem_naming_the_field()
    {
        var response = await fixture.Client.GetAsync("/api/rankings?view=best");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.True(problem.GetProperty("errors").TryGetProperty("view", out _));
    }

    // ---------------------------------------------------------------- AC-2 report card

    [DockerFact]
    public async Task A_report_card_reconciles_with_the_rows_it_describes()
    {
        await using var db = fixture.NewDbContext();
        var fips = await db.Bridges.AsNoTracking()
            .Where(b => b.RecordType == "1" && b.CountyCode != null)
            .Select(b => b.StateCode + b.CountyCode)
            .FirstAsync();

        var card = await GetJsonAsync($"/api/counties/{fips}");

        var good = card.GetProperty("good").GetInt32();
        var fair = card.GetProperty("fair").GetInt32();
        var poor = card.GetProperty("poor").GetInt32();
        var unrated = card.GetProperty("unrated").GetInt32();

        Assert.Equal(good + fair + poor, card.GetProperty("rated").GetInt32());
        Assert.Equal(good + fair + poor + unrated, card.GetProperty("structures").GetInt32());

        // Sliced outside the lambda: EF cannot translate a range expression inside an expression tree.
        var stateFips = fips[..2];
        var countyCode = fips[2..];
        var counted = await db.Bridges.CountAsync(
            b => b.RecordType == "1" && b.StateCode == stateFips && b.CountyCode == countyCode);
        Assert.Equal(counted, card.GetProperty("structures").GetInt32());
    }

    /// <summary>
    /// FR-1.5 AC-3: the ACS vintage is cited where the population appears, and the caveat that it is
    /// an estimate rather than a count travels with it.
    /// </summary>
    [DockerFact]
    public async Task A_report_card_cites_the_acs_vintage_beside_the_population()
    {
        var card = await GetJsonAsync("/api/counties/12086");
        var population = card.GetProperty("population");

        Assert.Equal(2738356, population.GetProperty("estimate").GetInt64());
        Assert.Equal(2024, population.GetProperty("acsVintage").GetInt32());
        Assert.Equal("2020-2024", population.GetProperty("acsPeriod").GetString());
        Assert.Contains("American Community Survey", population.GetProperty("citation").GetString());
        Assert.Contains("not a count", population.GetProperty("note").GetString());

        // The margin of error is suppressed by the Census for this county. Null, not zero — a zero
        // margin would claim the estimate is exact.
        Assert.Equal(JsonValueKind.Null, population.GetProperty("marginOfError").ValueKind);
    }

    /// <summary>
    /// A county with a boundary, no ACS row and no structures. Every derived figure is null rather
    /// than zero, and the citation says the Census publishes no estimate rather than citing one.
    /// </summary>
    [DockerFact]
    public async Task An_empty_county_publishes_absences_not_zeroes()
    {
        var card = await GetJsonAsync("/api/counties/60010");

        Assert.Equal("Eastern District", card.GetProperty("countyName").GetString());
        Assert.Equal(0, card.GetProperty("structures").GetInt32());
        Assert.Equal(JsonValueKind.Null, card.GetProperty("poorPercent").ValueKind);
        Assert.Equal(JsonValueKind.Null, card.GetProperty("medianYearBuilt").ValueKind);
        Assert.Equal(JsonValueKind.Null, card.GetProperty("population").GetProperty("estimate").ValueKind);
        Assert.Contains("No American Community Survey", card.GetProperty("population").GetProperty("citation").GetString());
    }

    /// <summary>
    /// A county code the Census retired still answers, with the fallback label and a citation that
    /// says why there is no population — not the island-areas sentence, which would be untrue here.
    /// </summary>
    /// <summary>
    /// A county code NBI publishes that the Census county set does not carry still answers, with the
    /// fallback label and a citation that says why there is no population — not the island-areas
    /// sentence, which would be untrue here.
    /// <para>
    /// The county is discovered rather than hardcoded, so the same test covers the fixture (whose
    /// bridges are Florida and whose Census set holds only Miami-Dade) and the national load, where
    /// this is Connecticut's eight retired codes and 4,362 structures.
    /// </para>
    /// </summary>
    [DockerFact]
    public async Task A_county_code_the_census_set_does_not_carry_answers_with_a_label_and_the_right_reason()
    {
        await using var db = fixture.NewDbContext();

        var known = await db.CensusCounties.AsNoTracking().Select(c => c.CountyFips).ToListAsync();
        var fips = await db.Bridges.AsNoTracking()
            .Where(b => b.RecordType == "1" && b.CountyCode != null)
            .Select(b => b.StateCode + b.CountyCode)
            .Distinct()
            .Where(f => !known.Contains(f))
            .OrderBy(f => f)
            .FirstOrDefaultAsync();

        // Asserted rather than skipped: both the fixture and the national load have such a county,
        // and one that stopped having one would mean the Census set had silently grown to cover
        // every published code — which would make this whole branch of the report card dead.
        Assert.NotNull(fips);

        var card = await GetJsonAsync($"/api/counties/{fips}");

        Assert.Equal(fips, card.GetProperty("countyFips").GetString());
        Assert.StartsWith("County FIPS ", card.GetProperty("countyName").GetString());
        Assert.True(card.GetProperty("structures").GetInt32() > 0);
        Assert.Contains(
            "no longer publishes a county with this code",
            card.GetProperty("population").GetProperty("citation").GetString());
    }

    [DockerFact]
    public async Task A_county_neither_publisher_knows_is_a_404_that_explains_itself()
    {
        var response = await fixture.Client.GetAsync("/api/counties/12999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Contains("zero counts", problem.GetProperty("detail").GetString());
    }

    [DockerFact]
    public async Task A_malformed_county_code_is_a_validation_problem_not_a_404()
    {
        var response = await fixture.Client.GetAsync("/api/counties/1208");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------------------------------------------------------------- AC-3 exports

    [DockerTheory]
    [InlineData("/api/rankings.csv?groupBy=county", "spansight-worst-condition-by-county-national.csv")]
    [InlineData("/api/rankings.csv?view=high-adt-poor&state=FL", "spansight-high-adt-poor-fl.csv")]
    [InlineData("/api/counties/12086.csv", "spansight-county-12086.csv")]
    public async Task An_export_is_served_as_a_named_csv_download(string url, string fileName)
    {
        var (_, response) = await GetCsvAsync(url);

        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType?.CharSet);
        // The filename must come from the server: in production the SPA and the API are different
        // origins, and browsers ignore an anchor's download attribute cross-origin.
        Assert.Equal(fileName, response.Content.Headers.ContentDisposition?.FileName);
    }

    /// <summary>
    /// GR-6 for the copy that leaves the building. The export carries the same rule the view is
    /// required to display, as leading comment lines — a ranking whose exclusions stayed behind in
    /// the browser is exactly the bypass FR-1.3's cadence caption turned out to be.
    /// </summary>
    [DockerFact]
    public async Task An_export_carries_the_same_definition_the_json_serves()
    {
        var ranking = await GetJsonAsync("/api/rankings?view=high-adt-poor&limit=5");
        var (csv, _) = await GetCsvAsync("/api/rankings.csv?view=high-adt-poor&limit=5");

        var definition = ranking.GetProperty("definition");
        foreach (var field in (ReadOnlySpan<string>)["headline", "sortedBy", "includes", "excludes", "note"])
        {
            Assert.Contains(definition.GetProperty(field).GetString()!, csv);
        }

        // Every line of the preamble is skippable by a reader that honours '#'.
        var preamble = csv.Split('\n').TakeWhile(l => l.StartsWith('#')).ToList();
        Assert.NotEmpty(preamble);
        Assert.Contains(preamble, l => l.Contains("Set aside by the rules above"));
    }

    /// <summary>The export is the ranking, not a differently-computed second answer.</summary>
    [DockerFact]
    public async Task An_export_holds_exactly_the_rows_the_json_returned()
    {
        var ranking = await GetJsonAsync("/api/rankings?view=high-adt-poor&limit=5");
        var (csv, _) = await GetCsvAsync("/api/rankings.csv?view=high-adt-poor&limit=5");

        var rows = DataRows(csv);
        var structures = ranking.GetProperty("structures").EnumerateArray().ToList();

        // One header row plus one row per structure.
        Assert.Equal(structures.Count + 1, rows.Count);

        for (var i = 0; i < structures.Count; i++)
        {
            var fields = rows[i + 1].Split(',');
            Assert.Equal(structures[i].GetProperty("rank").GetInt32().ToString(), fields[0]);
            Assert.Equal(structures[i].GetProperty("state").GetString(), fields[1]);
            Assert.Equal(structures[i].GetProperty("structureNumber").GetString(), fields[2]);
        }
    }

    [DockerFact]
    public async Task A_report_card_export_carries_the_counts_and_the_whole_history()
    {
        var card = await GetJsonAsync("/api/counties/12086");
        var (csv, _) = await GetCsvAsync("/api/counties/12086.csv");

        Assert.Contains($"structures,{card.GetProperty("structures").GetInt32()}", csv);
        Assert.Contains($"poor,{card.GetProperty("poor").GetInt32()}", csv);
        Assert.Contains(
            $"population_estimate,{card.GetProperty("population").GetProperty("estimate").GetInt64()}",
            csv);
        // Suppressed by the Census: an empty cell, never a zero.
        Assert.Contains("population_margin_of_error,\n", csv);

        var years = card.GetProperty("trend").GetProperty("points").GetArrayLength();
        Assert.Equal(years, DataRows(csv).Count(r => r.StartsWith("19") || r.StartsWith("20")));
    }

    /// <summary>
    /// AC-3 asks for the exports to be rate-limited. They sit in the same <c>/api</c> group as every
    /// other endpoint and so inherit the global fixed-window limiter rather than needing a mechanism
    /// of their own — which is only worth asserting if the assertion can fail.
    /// <para>
    /// Its own host with a permit limit of two, because the shared collection deliberately runs with
    /// the limiter effectively off: one 429 there would fail every test at once.
    /// </para>
    /// </summary>
    [DockerTheory]
    [InlineData("/api/rankings.csv?groupBy=county")]
    [InlineData("/api/counties/12086.csv")]
    public async Task An_export_is_refused_by_the_same_limiter_as_the_rest_of_the_api(string url)
    {
        using var host = new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<ApiAssemblyMarker>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:SpanSight", fixture.ConnectionString);
                builder.UseSetting("Otlp:Endpoint", "");
                builder.UseSetting("RateLimiting:PermitLimit", "2");
                builder.UseSetting("RateLimiting:WindowSeconds", "60");
            });

        using var client = host.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync(url)).StatusCode);
    }
}
