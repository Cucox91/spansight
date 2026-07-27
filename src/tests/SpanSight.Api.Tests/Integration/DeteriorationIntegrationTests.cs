using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using SpanSight.Core.Analytics;

namespace SpanSight.Api.Tests.Integration;

/// <summary>
/// FR-1.3 AC-1/AC-3/AC-4 against real PostGIS: the deterioration loader publishes the committed
/// fixture, then the API serves it.
/// <para>
/// Expectations come from <c>src/tests/fixtures/deterioration-aggregates/README.md</c>, which was
/// written by hand — nothing here is compared against the job that produces real matrices. The job is
/// golden-tested separately, so a bug in either half cannot make both agree.
/// </para>
/// </summary>
[Collection("postgis-api")]
public class DeteriorationIntegrationTests(PostgisApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string Cohort =
        "typeGroup=Girder%20%2F%20Stringer&materialGroup=Steel&region=Northeast";

    private async Task<JsonElement> GetJsonAsync(string url)
    {
        var response = await fixture.Client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    private static JsonElement Row(JsonElement matrix, int fromRating) =>
        matrix.GetProperty("rows").EnumerateArray()
            .Single(r => r.GetProperty("fromRating").GetInt32() == fromRating);

    private static JsonElement Cell(JsonElement row, int toRating) =>
        row.GetProperty("cells").EnumerateArray()
            .Single(c => c.GetProperty("toRating").GetInt32() == toRating);

    // ---------------------------------------------------------------- loader

    [DockerFact]
    public void The_seed_published_the_whole_fixture()
    {
        Assert.False(fixture.DeteriorationSeedSummary.Skipped);
        Assert.Equal(8, fixture.DeteriorationSeedSummary.MatrixRows);
        Assert.Equal(14, fixture.DeteriorationSeedSummary.Cells);
        Assert.Equal("deterioration-fixture-0001", fixture.DeteriorationSeedSummary.JobRunId);
    }

    [DockerFact]
    public async Task Reload_of_the_same_job_is_a_noop_and_force_converges()
    {
        var again = await fixture.LoadDeteriorationAsync(force: false);
        Assert.True(again.Skipped);

        var forced = await fixture.LoadDeteriorationAsync(force: true);
        Assert.False(forced.Skipped);

        await using var db = fixture.NewDbContext();
        Assert.Equal(8, await db.DeteriorationMatrixRows.CountAsync());
        Assert.Equal(14, await db.DeteriorationMatrixCells.CountAsync());
    }

    /// <summary>
    /// The published floor and the methodology version are stamped on the run, not hardcoded
    /// downstream — that is what lets the API apply the floor the matrices were actually judged by.
    /// </summary>
    [DockerFact]
    public async Task Every_published_row_is_stamped_with_its_run_and_its_methodology()
    {
        await using var db = fixture.NewDbContext();

        // Follow the rows, not the run table. Other tests in this collection publish and roll back
        // other jobs, so a Completed run with a higher id may be serving nothing at all — which is
        // exactly the case the API's own provenance lookup has to get right.
        var servingRunId = await db.DeteriorationMatrixRows.Select(r => r.DeteriorationRunId).Distinct().SingleAsync();
        var run = await db.DeteriorationRuns.SingleAsync(r => r.Id == servingRunId);

        Assert.Equal(DeteriorationRunStatus.Completed, run.Status);
        Assert.Equal("deterioration-fixture-0001", run.JobRunId);
        Assert.Equal("v1.1", run.MethodologyVersion);
        Assert.Equal(50, run.SampleFloor);
        Assert.NotNull(run.CompletedUtc);

        Assert.False(await db.DeteriorationMatrixRows.AnyAsync(r => r.DeteriorationRunId != run.Id));
        Assert.False(await db.DeteriorationMatrixCells.AnyAsync(c => c.DeteriorationRunId != run.Id));
    }

    [DockerFact]
    public async Task A_shrinking_job_output_removes_the_rows_it_no_longer_contains()
    {
        var staging = await WriteStagingAsync("deterioration-shrunk");
        try
        {
            var shrunk = await fixture.LoadDeteriorationAsync(force: false, directory: staging);

            Assert.Equal(1, shrunk.MatrixRows);
            Assert.Equal(1, shrunk.Cells);
            // 7 rows + 13 cells from the previous run had to go.
            Assert.Equal(20, shrunk.Superseded);

            await using var db = fixture.NewDbContext();
            Assert.Equal(1, await db.DeteriorationMatrixRows.CountAsync());
            Assert.Equal(1, await db.DeteriorationMatrixCells.CountAsync());
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
            await fixture.LoadDeteriorationAsync(force: true);
        }
    }

    [DockerFact]
    public async Task A_manifest_that_disagrees_with_its_data_is_refused()
    {
        var staging = await WriteStagingAsync("deterioration-mismatch", matrixRows: 999);
        try
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => fixture.LoadDeteriorationAsync(force: false, directory: staging));
            Assert.Contains("999", error.Message);

            await using var db = fixture.NewDbContext();
            var failed = await db.DeteriorationRuns.FirstAsync(r => r.JobRunId == "deterioration-mismatch");
            Assert.Equal(DeteriorationRunStatus.Failed, failed.Status);
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
            await fixture.LoadDeteriorationAsync(force: true);
        }
    }

    /// <summary>
    /// A span a row cannot possibly have is refused rather than served: the span is the only thing
    /// keeping a pooled cell from reading as 34 years of evidence, so a wrong one is not cosmetic.
    /// </summary>
    [DockerFact]
    public async Task A_row_whose_span_cannot_describe_its_pairs_is_refused()
    {
        var staging = await WriteStagingAsync("deterioration-badspan", yearPairsObserved: 9);
        try
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => fixture.LoadDeteriorationAsync(force: false, directory: staging));
            Assert.Contains("year-pairs", error.Message);
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
            await fixture.LoadDeteriorationAsync(force: true);
        }
    }

    // ---------------------------------------------------------------- matrix endpoint

    [DockerFact]
    public async Task A_cohort_matrix_returns_the_hand_written_counts()
    {
        var matrix = await GetJsonAsync($"/api/deterioration/matrix?component=Deck&{Cohort}");

        Assert.Equal("Deck", matrix.GetProperty("component").GetString());
        Assert.Equal(150, matrix.GetProperty("pairs").GetInt32());
        Assert.Equal(50, matrix.GetProperty("sampleFloor").GetInt32());
        Assert.False(matrix.GetProperty("cohort").GetProperty("isNational").GetBoolean());

        var row = Row(matrix, 5);
        Assert.Equal(51, row.GetProperty("rowTotal").GetInt32());
        Assert.Equal(50, Cell(row, 5).GetProperty("pairs").GetInt32());
        Assert.Equal(1, Cell(row, 4).GetProperty("pairs").GetInt32());
    }

    /// <summary>
    /// AC-3, at the API boundary. 49 is below the floor and 50 is exactly on it, so this fails for a
    /// strict-greater-than comparison as readily as for a missing one.
    /// </summary>
    [DockerTheory]
    [InlineData(5, 51, true)]
    [InlineData(7, 50, true)]   // exactly at n = 50: the floor is >=, so rates publish
    [InlineData(6, 49, false)]  // one pair short: every rate in the row is suppressed
    public async Task The_sample_size_floor_decides_whether_rates_are_published(
        int fromRating, int rowTotal, bool sufficient)
    {
        var row = Row(await GetJsonAsync($"/api/deterioration/matrix?component=Deck&{Cohort}"), fromRating);

        Assert.Equal(rowTotal, row.GetProperty("rowTotal").GetInt32());
        Assert.Equal(sufficient, row.GetProperty("sufficient").GetBoolean());

        foreach (var cell in row.GetProperty("cells").EnumerateArray())
        {
            var rate = cell.GetProperty("rate");
            if (sufficient)
            {
                Assert.NotEqual(JsonValueKind.Null, rate.ValueKind);
            }
            else
            {
                // Never a number the UI could render as a percentage — not even 0.
                Assert.Equal(JsonValueKind.Null, rate.ValueKind);
            }
        }
    }

    /// <summary>Suppression removes the rate, never the evidence (methodology §4).</summary>
    [DockerFact]
    public async Task A_suppressed_row_still_reports_its_counts_and_span()
    {
        var row = Row(await GetJsonAsync($"/api/deterioration/matrix?component=Deck&{Cohort}"), 6);

        Assert.False(row.GetProperty("sufficient").GetBoolean());
        Assert.Equal(49, row.GetProperty("rowTotal").GetInt32());
        Assert.Equal(45, Cell(row, 6).GetProperty("pairs").GetInt32());
        Assert.Equal(4, Cell(row, 5).GetProperty("pairs").GetInt32());
        Assert.Equal(2020, row.GetProperty("firstFromYear").GetInt32());
        Assert.Equal(2022, row.GetProperty("lastFromYear").GetInt32());
        Assert.Equal(3, row.GetProperty("yearPairsObserved").GetInt32());
    }

    [DockerFact]
    public async Task Rates_are_computed_from_the_stored_counts_and_sum_to_one_hundred()
    {
        var row = Row(await GetJsonAsync($"/api/deterioration/matrix?component=Deck&{Cohort}"), 7);

        Assert.Equal(20.0, Cell(row, 6).GetProperty("rate").GetDouble(), 1);
        Assert.Equal(80.0, Cell(row, 7).GetProperty("rate").GetDouble(), 1);
        Assert.Equal(100.0, row.GetProperty("cells").EnumerateArray().Sum(c => c.GetProperty("rate").GetDouble()), 1);
    }

    /// <summary>
    /// All ten from-ratings and all ten to-ratings come back whether observed or not: an omitted row or
    /// a blank cell is indistinguishable from a rendering bug, and an observed zero is a different
    /// fact from a cell 34 years never produced.
    /// </summary>
    [DockerFact]
    public async Task The_grid_is_always_ten_by_ten()
    {
        var matrix = await GetJsonAsync($"/api/deterioration/matrix?component=Deck&{Cohort}");
        var rows = matrix.GetProperty("rows").EnumerateArray().ToArray();

        Assert.Equal(10, rows.Length);
        Assert.Equal(Enumerable.Range(0, 10), rows.Select(r => r.GetProperty("fromRating").GetInt32()));
        Assert.All(rows, r => Assert.Equal(10, r.GetProperty("cells").GetArrayLength()));

        // An empty row is present and honest rather than absent.
        var empty = Row(matrix, 0);
        Assert.Equal(0, empty.GetProperty("rowTotal").GetInt32());
        Assert.False(empty.GetProperty("sufficient").GetBoolean());
        Assert.Equal(0, empty.GetProperty("yearPairsObserved").GetInt32());

        // A zero inside a published row is a real zero, with a real 0.0% beside it.
        var published = Row(matrix, 7);
        Assert.Equal(0, Cell(published, 3).GetProperty("pairs").GetInt32());
        Assert.Equal(0.0, Cell(published, 3).GetProperty("rate").GetDouble(), 1);
    }

    [DockerFact]
    public async Task The_national_matrix_is_reached_by_omitting_the_cohort()
    {
        var national = await GetJsonAsync("/api/deterioration/matrix?component=Deck");

        Assert.True(national.GetProperty("cohort").GetProperty("isNational").GetBoolean());
        Assert.Equal("All", national.GetProperty("cohort").GetProperty("region").GetString());
        Assert.Equal(150, national.GetProperty("pairs").GetInt32());
    }

    /// <summary>
    /// The four families are independent populations and must never be conflated: asking for Culvert
    /// must not return the Deck cohort's numbers (22,967 pair units publish both in the real data).
    /// </summary>
    [DockerFact]
    public async Task Component_families_do_not_bleed_into_each_other()
    {
        var culvert = await GetJsonAsync("/api/deterioration/matrix?component=Culvert");

        Assert.Equal(10, culvert.GetProperty("pairs").GetInt32());
        Assert.Equal(10, Cell(Row(culvert, 6), 6).GetProperty("pairs").GetInt32());
        Assert.Equal(0, Row(culvert, 7).GetProperty("rowTotal").GetInt32());
    }

    /// <summary>GR-6 as a served contract: no matrix response can arrive without its framing.</summary>
    [DockerTheory]
    [InlineData("/api/deterioration/matrix?component=Deck")]
    [InlineData("/api/deterioration/matrix?component=Culvert")]
    public async Task Every_matrix_carries_its_method_note_link_and_provenance(string url)
    {
        var matrix = await GetJsonAsync(url);

        Assert.Contains("not engineering advice", matrix.GetProperty("method").GetString());
        Assert.Contains("not a prediction", matrix.GetProperty("method").GetString());
        Assert.Contains("24-month", matrix.GetProperty("cadenceCaption").GetString());
        Assert.Equal("v1.1", matrix.GetProperty("methodologyVersion").GetString());
        Assert.EndsWith("METHODOLOGY-DETERIORATION.md", matrix.GetProperty("methodologyUrl").GetString());
        Assert.Equal("deterioration-fixture-0001",
            matrix.GetProperty("provenance").GetProperty("jobRunId").GetString());
    }

    /// <summary>
    /// The caption's number is measured on the matrix being returned, so it cannot disagree with it —
    /// and it is measured over the above-floor rows only.
    /// <para>
    /// The Deck cohort has 150 pairs across three rows, but the 49-pair row is suppressed, so the
    /// share is the diagonal of the other two: (50 + 40) / (51 + 50) = 89.1%, not the 90.0% a
    /// whole-matrix computation gives. A caption that averaged in suppressed evidence would be
    /// publishing a rate the same response declined to publish cell by cell.
    /// </para>
    /// </summary>
    [DockerFact]
    public async Task The_cadence_caption_reports_the_share_of_the_rows_that_clear_the_floor()
    {
        var deck = await GetJsonAsync($"/api/deterioration/matrix?component=Deck&{Cohort}");

        Assert.Equal(89.1, deck.GetProperty("unchangedShare").GetDouble(), 1);
        Assert.Contains("89.1%", deck.GetProperty("cadenceCaption").GetString());
        Assert.Contains("above the sample-size floor", deck.GetProperty("cadenceCaption").GetString());
    }

    /// <summary>
    /// AC-3's last hiding place. The Culvert fixture holds 10 pairs against a floor of 50, so every
    /// row is suppressed — and the matrix-level share must be suppressed with them. Computed over the
    /// whole matrix it would be 100.0%, which is exactly the diagonal rate of the one row the
    /// response just returned as null: a page that says "insufficient data" ten times and then states
    /// a percentage. In the 2026-07-26 build 395 of 1,028 cohort matrices are in this state.
    /// </summary>
    [DockerFact]
    public async Task A_matrix_with_no_row_above_the_floor_publishes_no_share_at_all()
    {
        var culvert = await GetJsonAsync("/api/deterioration/matrix?component=Culvert");

        Assert.Equal(10, culvert.GetProperty("pairs").GetInt32());
        Assert.All(culvert.GetProperty("rows").EnumerateArray(),
            r => Assert.False(r.GetProperty("sufficient").GetBoolean()));

        Assert.Equal(JsonValueKind.Null, culvert.GetProperty("unchangedShare").ValueKind);

        // Not a number anywhere in the caption, and not the word "None" standing where one belongs.
        var caption = culvert.GetProperty("cadenceCaption").GetString()!;
        Assert.DoesNotContain('%', caption);
        Assert.Contains("no share is stated", caption);

        // The whole response body carries no percentage for a matrix this thin.
        var body = await (await fixture.Client.GetAsync("/api/deterioration/matrix?component=Culvert"))
            .Content.ReadAsStringAsync();
        Assert.DoesNotContain("100.0", body);
    }

    /// <summary>
    /// The method note states the range of the run being served, not a hardcoded one. The fixture is
    /// 2020–2023; a note claiming 1992–2025 beside provenance saying three vintage pairs would be the
    /// AC-4 artefact contradicting the numbers it travels with.
    /// </summary>
    [DockerFact]
    public async Task The_method_note_states_the_serving_run_vintage_range()
    {
        var matrix = await GetJsonAsync("/api/deterioration/matrix?component=Deck");
        var method = matrix.GetProperty("method").GetString()!;

        Assert.Contains("2020–2023", method);
        Assert.DoesNotContain("1992", method);
    }

    [DockerFact]
    public async Task A_cohort_with_no_published_pairs_returns_an_empty_grid_not_an_error()
    {
        var matrix = await GetJsonAsync(
            "/api/deterioration/matrix?component=Deck&typeGroup=Truss%20%2F%20Arch&materialGroup=Masonry&region=West");

        Assert.Equal(0, matrix.GetProperty("pairs").GetInt32());
        Assert.Equal(10, matrix.GetProperty("rows").GetArrayLength());
        Assert.All(matrix.GetProperty("rows").EnumerateArray(),
            r => Assert.False(r.GetProperty("sufficient").GetBoolean()));
        Assert.Equal(JsonValueKind.Null, matrix.GetProperty("unchangedShare").ValueKind);
    }

    [DockerTheory]
    [InlineData("/api/deterioration/matrix", "component")]
    [InlineData("/api/deterioration/matrix?component=Girders", "component")]
    // Enum.TryParse accepts any numeric string and yields an undefined enum value, so without an
    // IsDefined check these returned 200 with a fabricated family label over an empty grid —
    // indistinguishable from a real family that published nothing.
    [InlineData("/api/deterioration/matrix?component=99", "component")]
    [InlineData("/api/deterioration/matrix?component=0", "component")]
    [InlineData("/api/deterioration/matrix?component=-3", "component")]
    [InlineData("/api/deterioration/matrix?component=Deck&typeGroup=Culvert", "typeGroup")]
    [InlineData("/api/deterioration/matrix?component=Deck&typeGroup=Nope&materialGroup=Steel&region=West", "typeGroup")]
    [InlineData("/api/deterioration/matrix?component=Deck&typeGroup=Culvert&materialGroup=Nope&region=West", "materialGroup")]
    [InlineData("/api/deterioration/matrix?component=Deck&typeGroup=Culvert&materialGroup=Steel&region=Nope", "region")]
    public async Task Bad_requests_return_a_named_validation_problem(string url, string expectedField)
    {
        var response = await fixture.Client.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.True(problem.GetProperty("errors").TryGetProperty(expectedField, out _),
            $"Expected a validation error naming '{expectedField}' for {url}.");
    }

    // ---------------------------------------------------------------- cohorts endpoint

    [DockerFact]
    public async Task The_catalog_lists_the_cohorts_with_their_pair_counts()
    {
        var catalog = await GetJsonAsync("/api/deterioration/cohorts");

        Assert.Equal(50, catalog.GetProperty("sampleFloor").GetInt32());
        Assert.Equal("v1.1", catalog.GetProperty("methodologyVersion").GetString());

        var cohorts = catalog.GetProperty("cohorts").EnumerateArray().ToArray();
        // Two cohorts plus the national sentinel, which sorts first.
        Assert.Equal(3, cohorts.Length);
        Assert.True(cohorts[0].GetProperty("isNational").GetBoolean());

        var steel = cohorts.Single(c => c.GetProperty("materialGroup").GetString() == "Steel");
        var deck = steel.GetProperty("components").EnumerateArray()
            .Single(c => c.GetProperty("component").GetString() == "Deck");

        Assert.Equal(150, deck.GetProperty("pairs").GetInt32());
        Assert.Equal(3, deck.GetProperty("rows").GetInt32());
        Assert.Equal(2, deck.GetProperty("rowsAboveFloor").GetInt32());
    }

    /// <summary>
    /// Every group ships the published codes it collapses, so a view can be honest that "Other"
    /// contains aluminium and cast iron rather than leaving the label to imply otherwise.
    /// </summary>
    [DockerFact]
    public async Task The_catalog_publishes_each_group_with_its_member_codes()
    {
        var dimensions = (await GetJsonAsync("/api/deterioration/cohorts")).GetProperty("dimensions");

        Assert.Equal(
            ["Deck", "Superstructure", "Substructure", "Culvert"],
            dimensions.GetProperty("components").EnumerateArray().Select(c => c.GetString()));

        var other = dimensions.GetProperty("materialGroups").EnumerateArray()
            .Single(g => g.GetProperty("name").GetString() == "Other");
        var codes = other.GetProperty("codes").EnumerateArray()
            .ToDictionary(c => c.GetProperty("code").GetString()!, c => c.GetProperty("label").GetString());

        Assert.Equal(["0", "9"], codes.Keys.Order());
        Assert.Equal("Aluminum, wrought iron, or cast iron", codes["9"]);

        // The ten cohort buckets, including the two the methodology names explicitly.
        var regions = dimensions.GetProperty("regions").EnumerateArray().Select(r => r.GetString()).ToArray();
        Assert.Equal(10, regions.Length);
        Assert.Contains("Outside contiguous U.S.", regions);
        Assert.Contains("Not published",
            dimensions.GetProperty("typeGroups").EnumerateArray().Select(g => g.GetProperty("name").GetString()));
    }

    /// <summary>
    /// GR-6, structurally: FR-1.3 has no per-structure surface. Nothing under
    /// <c>/api/deterioration</c> accepts a structure number, and nothing it returns identifies one.
    /// </summary>
    [DockerTheory]
    [InlineData("/api/deterioration/cohorts")]
    [InlineData("/api/deterioration/matrix?component=Deck")]
    public async Task No_deterioration_response_identifies_a_structure(string url)
    {
        var body = await (await fixture.Client.GetAsync(url)).Content.ReadAsStringAsync();

        foreach (var forbidden in (ReadOnlySpan<string>)["structureNumber", "1213483000", "stateCode", "\"state\""])
        {
            Assert.DoesNotContain(forbidden, body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [DockerFact]
    public async Task A_structure_keyed_deterioration_route_does_not_exist()
    {
        var response = await fixture.Client.GetAsync("/api/deterioration/FL/1213483000");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// Writes a one-row job output, optionally with a manifest or span that must be refused. Kept in
    /// one place so each failure test differs only in the thing it breaks.
    /// </summary>
    private static async Task<string> WriteStagingAsync(
        string runId, int matrixRows = 1, int yearPairsObserved = 3)
    {
        var staging = Path.Combine(Path.GetTempPath(), $"spansight-det-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);

        await File.WriteAllTextAsync(Path.Combine(staging, "matrix_row.csv"),
            "component,type_group,material_group,region,from_rating,row_total,first_from_year,last_from_year,year_pairs_observed\n" +
            $"Deck,All,All,All,5,51,2020,2022,{yearPairsObserved}\n");
        await File.WriteAllTextAsync(Path.Combine(staging, "matrix_cell.csv"),
            "component,type_group,material_group,region,from_rating,to_rating,pairs\n" +
            "Deck,All,All,All,5,5,51\n");
        await File.WriteAllTextAsync(Path.Combine(staging, "manifest.json"),
            $$"""
              {"runId":"{{runId}}","catalogSha256":"deadbeef","methodologyVersion":"v1.1","sampleFloor":50,
               "firstYear":2020,"lastYear":2023,"yearPairs":3,"structurePairs":51,"componentPairs":51,
               "cohorts":1,"matrixRows":{{matrixRows}},"cells":1,"rowsBelowFloor":0}
              """);

        return staging;
    }
}
