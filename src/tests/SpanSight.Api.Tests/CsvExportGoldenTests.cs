using System.Text;

using SpanSight.Api;
using SpanSight.Api.Endpoints;
using SpanSight.Api.Export;

namespace SpanSight.Api.Tests;

/// <summary>
/// FR-1.4 AC-3 — the server-generated CSV exports, compared byte for byte against committed
/// fixtures.
/// <para>
/// The inputs here are hand-built DTOs with deliberately short definition strings. That is the
/// point of the split: these tests pin the <em>serialization</em> — quoting, escaping, number
/// format, null handling, the comment block, LF endings — and would otherwise churn every time a
/// sentence of production copy was improved. That the real definition text reaches the file is
/// asserted separately, over HTTP, in <c>RankingIntegrationTests</c>.
/// </para>
/// <para>
/// The fixtures are written by hand, not blessed from output. A golden file regenerated from the
/// code it is meant to check agrees with any bug that code has.
/// </para>
/// </summary>
public class CsvExportGoldenTests
{
    private static string Golden(string name) => File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "fixtures", "csv-exports", name));

    /// <summary>
    /// Normalised only for line endings, and only on the expected side. A checkout that rewrote the
    /// fixture to CRLF should not fail the suite — <c>.gitattributes</c> marks these files binary so
    /// it should not happen — but the *produced* side is asserted raw, because emitting CRLF is a
    /// real defect that would make the export host-dependent.
    /// </summary>
    private static void AssertMatches(string goldenName, CsvExport actual)
    {
        var expected = Golden(goldenName).ReplaceLineEndings("\n");
        Assert.Equal(expected, actual.ToString());
        Assert.DoesNotContain('\r', actual.ToString());
    }

    // ------------------------------------------------------------------ rankings

    private static RankingDto GroupRanking() => new(
        "worst-condition",
        "county",
        2025,
        "Florida",
        new RankingDefinitionDto(
            "Top 2 counties, in Florida",
            "Poor share, highest first.",
            "Record type 1 with at least 50 rated structures.",
            "3 counties fall below the minimum.",
            "Rated structures in the group.",
            50,
            3,
            91,
            "Published inspection ratings, ordered. Not engineering advice."),
        [
            // A real county name containing a comma — the case a naive join on ',' breaks on.
            new RankingGroupDto(1, "35013", "Doña Ana County, NM", "35013", 120, 118, 40, 38, 40, 2, 33.9),
            // A cohort key containing pipes and an em dash, and a null fips. Pipes must NOT be
            // quoted: the whole toolchain reads DuckDB's pipe-delimited output elsewhere, and a
            // reader who assumes this file is pipe-delimited too is not the file's problem.
            new RankingGroupDto(2, "Steel|Truss / Arch|South", "Steel Truss / Arch — South", null,
                90, 88, 30, 30, 28, 2, 31.8),
        ],
        [],
        "/api/rankings.csv?view=worst-condition&groupBy=county&limit=2");

    private static RankingDto StructureRanking() => new(
        "high-adt-poor",
        null,
        2025,
        null,
        new RankingDefinitionDto(
            "Top 2 structures, nationally",
            "Published traffic, highest first.",
            "Record type 1 in Poor condition with a published traffic count.",
            "13 Poor structures publish no traffic count.",
            // Null denominator and null minimum: a structure-level list has neither, and the two
            // comment lines must be omitted rather than emitted empty.
            null,
            null,
            0,
            13,
            "Published inspection ratings, ordered. Not engineering advice."),
        [],
        [
            // Embedded double quotes in a published free-text item (NBI item 7).
            new RankingStructureDto(1, "FL", "12", "860001", "12086", "Miami-Dade County", 120000,
                "Poor", 4, 1968, "I-95 \"EXPRESS\" LANES", "MIAMI RIVER"),
            // A Connecticut structure: no Census name for its published county code, and no rating,
            // year or free text — every nullable column empty in one row.
            new RankingStructureDto(2, "CT", "09", "001234", "09003", null, 850,
                "Poor", null, null, null, null),
        ],
        "/api/rankings.csv?view=high-adt-poor&limit=2");

    [Fact]
    public void A_group_ranking_export_is_byte_for_byte_the_committed_fixture()
    {
        AssertMatches("ranking-groups.csv", RankingEndpoints.WriteCsv(GroupRanking()));
    }

    [Fact]
    public void A_structure_ranking_export_is_byte_for_byte_the_committed_fixture()
    {
        AssertMatches("ranking-structures.csv", RankingEndpoints.WriteCsv(StructureRanking()));
    }

    // ------------------------------------------------------------------ report cards

    private static CountyPopulationDto Population(long? estimate, string? period) => new(
        estimate,
        null,
        period is null ? null : (short)2024,
        period,
        period is null ? null : "B01003",
        period is null
            ? "No ACS estimate is published for this county."
            : "U.S. Census Bureau, 2020-2024 ACS 5-Year Estimates, table B01003.",
        "An estimate, not a count.");

    [Fact]
    public void A_report_card_export_is_byte_for_byte_the_committed_fixture()
    {
        var card = new CountyReportCardDto(
            "35013", "Doña Ana County", "35", "NM", "New Mexico", 2025,
            120, 118, 40, 38, 40, 2,
            33.9, 33.9, 32.2,
            1974, 4_030_000,
            Population(224_266, "2020-2024"),
            new TrendSeriesDto(
                "County", "35013", "Doña Ana County", 2024, 2025,
                [
                    new TrendPointDto(2024, 119, 41, 38, 38, 2, 34.5, 31.9, 31.9, 1.7),
                    new TrendPointDto(2025, 120, 40, 38, 40, 2, 33.3, 31.7, 33.3, 1.7),
                ],
                "Years FHWA did not publish are omitted.",
                new TrendProvenanceDto("trends-fixture-0001", "abc", null)),
            "Current serving inventory, record type 1.",
            "Published inspection ratings. Not engineering advice.",
            "/api/counties/35013.csv");

        AssertMatches("county-report-card.csv", CountyEndpoints.WriteCsv(card));
    }

    /// <summary>
    /// The county with nothing in it: no structures, no population, no history. Every derived cell
    /// is empty rather than <c>0</c> — a share that could not be computed and a share that is
    /// genuinely zero are different facts, and a spreadsheet will average them together if they
    /// look the same (GR-6). The history block is omitted entirely rather than emitted headerless.
    /// </summary>
    [Fact]
    public void An_empty_county_export_writes_absences_not_zeroes()
    {
        var card = new CountyReportCardDto(
            "60010", "Eastern District", "60", "AS", "American Samoa", 2025,
            0, 0, 0, 0, 0, 0,
            null, null, null,
            null, null,
            Population(null, null),
            null,
            "Current serving inventory, record type 1.",
            "Published inspection ratings. Not engineering advice.",
            "/api/counties/60010.csv");

        AssertMatches("county-report-card-empty.csv", CountyEndpoints.WriteCsv(card));
    }

    // ------------------------------------------------------------------ the writer itself

    [Theory]
    [InlineData("plain", "plain", "no special characters, no quoting")]
    [InlineData("has,comma", "\"has,comma\"", "a comma would otherwise split the row")]
    [InlineData("has\"quote", "\"has\"\"quote\"", "quotes are doubled inside a quoted field")]
    [InlineData("has|pipe", "has|pipe", "a pipe is not a CSV delimiter and must not be quoted")]
    [InlineData("", "", "an empty value stays empty rather than becoming a quoted empty string")]
    [InlineData(null, "", "a null renders as an absence, identically to an empty string")]
    public void Values_are_escaped_by_the_rfc_4180_rules(string? value, string expected, string why)
    {
        Assert.Equal($"{expected}\n", new CsvExport().Row(value).ToString());
        Assert.NotEmpty(why);
    }

    /// <summary>
    /// A newline inside a value is flattened, not quoted. Legal RFC 4180 either way, and the single
    /// most common way a downstream reader mis-parses a file — the only free text this API exports
    /// is NBI items 6A/7, published as one line each.
    /// </summary>
    [Fact]
    public void A_newline_inside_a_value_is_flattened_rather_than_wrapped()
    {
        var csv = new CsvExport().Row("two\nlines").ToString();

        Assert.Equal("two lines\n", csv);
        Assert.Equal(2, csv.Split('\n').Length);
    }

    /// <summary>A comment carrying a newline would produce a line the reader cannot skip.</summary>
    [Fact]
    public void A_comment_cannot_introduce_an_unskippable_line()
    {
        Assert.Equal("# one two\n", new CsvExport().Comment("one\ntwo").ToString());
    }

    /// <summary>
    /// Numbers are invariant and ungrouped. A host with a comma group separator would otherwise turn
    /// one column into two, and a comma decimal separator would make the file locale-dependent —
    /// which is exactly what a byte-for-byte fixture exists to prevent.
    /// </summary>
    [Fact]
    public void Numbers_are_invariant_and_never_grouped()
    {
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");

            Assert.Equal("1234567", CsvExport.Number(1_234_567));
            Assert.Equal("33.9", CsvExport.Percent(33.9));
            Assert.Equal("0.0", CsvExport.Percent(0));
            Assert.Equal(string.Empty, CsvExport.Percent(null));
            Assert.Equal(string.Empty, CsvExport.Number((int?)null));
            Assert.Equal(string.Empty, CsvExport.Number((long?)null));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }

    /// <summary>
    /// UTF-8 without a BOM. County names carry diacritics — Doña Ana, Bayamón — and a BOM would
    /// corrupt the first header cell for every reader that does not strip it.
    /// </summary>
    [Fact]
    public void The_download_is_utf8_without_a_byte_order_mark()
    {
        var bytes = Encoding.UTF8.GetBytes(RankingEndpoints.WriteCsv(GroupRanking()).ToString());

        Assert.False(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.Contains("Doña Ana", Encoding.UTF8.GetString(bytes));
    }
}
