using System.Text;

using SpanSight.Core.Vintages;

namespace SpanSight.Core.Tests.Vintages;

/// <summary>
/// Golden conversion over the committed era fixtures (FR-1.1 AC-3): 300 real rows from each of the
/// 1992, 2010 and 2025 national files, including the rows that break the published dialect
/// (unescaped apostrophes) and culvert records that carry 'N' for items 58-60.
/// <para>
/// Fixture-only and dependency-free, so it runs in the ordinary <c>dotnet test</c> job.
/// </para>
/// </summary>
public class VintageFixtureGoldenTests
{
    // One entry per published column layout, matching the fixtures on disk and the SRS v1.4
    // amendment to FR-1.1 AC-3. 2016 (adds CAT10) and 2017 (adds CAT23/CAT29) are the two layouts
    // that motivated that amendment, so leaving them out of the golden theory would have left the
    // shell-script conversion in CI as their only coverage.
    public static TheoryData<int, VintageEra, int> Vintages => new()
    {
        { 1992, VintageEra.TenYearRule, 134 },
        { 2010, VintageEra.SufficiencyRating, 133 },
        { 2016, VintageEra.TenYearRule, 135 },
        { 2017, VintageEra.TenYearRule, 137 },
        { 2025, VintageEra.PerformanceMeasures, 123 },
    };

    [Theory]
    [MemberData(nameof(Vintages))]
    public async Task Fixture_converts_clean_and_reconciles(int year, VintageEra era, int sourceColumns)
    {
        var summary = await ConvertAsync(year);

        Assert.Equal(era.ToString(), summary.Summary.Era);
        Assert.Equal(sourceColumns, summary.Summary.SourceColumns);
        Assert.Equal(300, summary.Summary.RowsRead);
        Assert.Equal(300, summary.Summary.RowsConverted);
        Assert.Equal(0, summary.Summary.RowsRejected);
        Assert.True(summary.Summary.Reconciles);
        Assert.Empty(summary.Summary.RejectsByReason);
    }

    public static TheoryData<int> VintageYears => [1992, 2010, 2016, 2017, 2025];

    [Theory]
    [MemberData(nameof(VintageYears))]
    public async Task Every_vintage_emits_the_same_columns_in_the_same_order(int year)
    {
        var result = await ConvertAsync(year);

        var expected = string.Join(",", VintageConverter.OutputColumns);
        Assert.Equal(expected, result.NormalizedLines[0]);

        // 4 provenance columns + the full superset, whatever the source carried.
        Assert.Equal(VintageSchema.ProvenanceColumns.Count + VintageSchema.Columns.Count,
            VintageConverter.OutputColumns.Count);

        foreach (var line in result.NormalizedLines.Skip(1))
        {
            Assert.Equal(VintageConverter.OutputColumns.Count, CountCsvFields(line));
        }
    }

    [Theory]
    [InlineData(1992, "01", "0000000011074Z0", "5", "RELIEF BRIDGE", "1974")]
    [InlineData(2010, "01", "000232", "N", "BEAR CREEK", "1924")]
    [InlineData(2025, "01", "00000BLN0BR0010", "N", "TOWN CREEK", "1974")]
    public async Task Spot_values_survive_normalization(
        int year, string state, string structureNumber, string deck, string features, string yearBuilt)
    {
        var result = await ConvertAsync(year);
        var row = result.Rows[0];

        Assert.Equal(state, row[VintageSchema.StateCode]);
        Assert.Equal(structureNumber, row[VintageSchema.StructureNumber]);
        Assert.Equal(deck, row["DECK_COND_058"]);
        Assert.Equal(features, row["FEATURES_DESC_006A"]);   // qualifier + era padding stripped
        Assert.Equal(yearBuilt, row["YEAR_BUILT_027"]);
    }

    [Theory]
    [MemberData(nameof(VintageYears))]
    public async Task Provenance_is_stamped_on_every_row(int year)
    {
        var result = await ConvertAsync(year);

        foreach (var row in result.Rows)
        {
            Assert.Equal(year.ToString(), row["VINTAGE_YEAR"]);
            Assert.Equal($"nbi_{year}.txt", row["SOURCE_FILE"]);
            Assert.Equal("fixture-sha", row["SOURCE_SHA256"]);
        }

        // SOURCE_ROW is the physical line, so a reject can be found in the original file.
        Assert.Equal("2", result.Rows[0]["SOURCE_ROW"]);
        Assert.Equal("301", result.Rows[^1]["SOURCE_ROW"]);
    }

    [Fact]
    public async Task Older_eras_carry_sufficiency_rating_and_no_performance_columns()
    {
        var older = await ConvertAsync(1992);
        var newer = await ConvertAsync(2025);

        Assert.NotNull(older.Rows[0]["SUFFICIENCY_RATING"]);
        Assert.Equal(string.Empty, newer.Rows[0]["SUFFICIENCY_RATING"]);   // absent → NULL

        Assert.Equal(string.Empty, older.Rows[0]["BRIDGE_CONDITION"]);
        Assert.NotEqual(string.Empty, newer.Rows[0]["BRIDGE_CONDITION"]);

        // Only 1992 carries the 10-year-rule columns.
        Assert.NotNull(older.Rows[0]["STATUS_WITH_10YR_RULE"]);
        Assert.Equal(string.Empty, newer.Rows[0]["STATUS_WITH_10YR_RULE"]);
    }

    [Fact]
    public async Task Rows_whose_text_contains_an_unescaped_apostrophe_convert_intact()
    {
        // The fixtures lead with real 1992/2010 rows carrying values like 'LITTLE BASSETT'S CREEK'.
        var result = await ConvertAsync(1992);

        Assert.Contains(result.Rows, r => (r["FEATURES_DESC_006A"] + r["FACILITY_CARRIED_007"]).Contains('\''));
        Assert.All(result.Rows, r => Assert.False(string.IsNullOrWhiteSpace(r[VintageSchema.StructureNumber])));
    }

    [Fact]
    public async Task Culvert_records_keep_N_in_items_58_to_60_and_a_rating_in_62()
    {
        var result = await ConvertAsync(2025);
        var culverts = result.Rows.Where(r => r["DECK_COND_058"] == "N").ToList();

        Assert.NotEmpty(culverts);
        Assert.All(culverts, r => Assert.NotEqual(string.Empty, r["CULVERT_COND_062"]));
    }

    private static string FixturePath(int year) => Path.Combine("fixtures", "vintages", $"nbi_{year}.txt");

    private sealed record ConversionResult(
        VintageConversionSummary Summary,
        IReadOnlyList<string> NormalizedLines,
        IReadOnlyList<IReadOnlyDictionary<string, string>> Rows);

    private static async Task<ConversionResult> ConvertAsync(int year)
    {
        await using var source = File.OpenRead(FixturePath(year));
        var normalized = new StringWriter();
        var rejects = new StringWriter();

        var summary = await new VintageConverter().ConvertAsync(
            source, normalized, rejects, year, $"nbi_{year}.txt", "fixture-sha");

        var lines = normalized.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r'))
            .ToList();

        var header = ParseCsv(lines[0]);
        var rows = lines.Skip(1)
            .Select(l => (IReadOnlyDictionary<string, string>)header
                .Zip(ParseCsv(l))
                .ToDictionary(p => p.First, p => p.Second, StringComparer.OrdinalIgnoreCase))
            .ToList();

        return new ConversionResult(summary, lines, rows);
    }

    private static int CountCsvFields(string line) => ParseCsv(line).Count;

    /// <summary>Minimal RFC 4180 reader — the intermediate is what DuckDB consumes, so it must be valid.</summary>
    private static List<string> ParseCsv(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"' && current.Length == 0)
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return fields;
    }
}
