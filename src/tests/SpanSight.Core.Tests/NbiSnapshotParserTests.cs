using System.Text;

using SpanSight.Core.Ingestion;

using Xunit;

namespace SpanSight.Core.Tests;

public class NbiSnapshotParserTests
{
    private static async Task<List<NbiRowResult>> ParseAsync(string csv)
    {
        var parser = new NbiSnapshotParser();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var results = new List<NbiRowResult>();
        await foreach (var row in parser.ParseAsync(stream))
        {
            results.Add(row);
        }

        return results;
    }

    private const string Header =
        "STATE_CODE_001,COUNTY_CODE_003,RECORD_TYPE_005A,STRUCTURE_NUMBER_008,LAT_016,LONG_017,YEAR_BUILT_027,DECK_COND_058";

    [Fact]
    public async Task Maps_columns_by_header_name()
    {
        var rows = await ParseAsync(Header + "\n12,086,1,124077C,25470000,080130000,1972,6\n");

        var record = Assert.Single(rows).Record;
        Assert.NotNull(record);
        Assert.Equal("12", record.StateCode);
        Assert.Equal("086", record.CountyCode);
        Assert.Equal("124077C", record.StructureNumber);
        Assert.Equal("25470000", record.RawLatitude);
        Assert.Equal("1972", record.RawYearBuilt);
        Assert.Equal("6", record.DeckCondition);
    }

    [Fact]
    public async Task Header_matching_is_case_insensitive()
    {
        var rows = await ParseAsync(Header.ToLowerInvariant() + "\n12,086,1,ABC,25470000,080130000,1972,6\n");

        Assert.NotNull(Assert.Single(rows).Record);
    }

    [Fact]
    public async Task Missing_required_column_throws_NbiFormatException()
    {
        var noLat = "STATE_CODE_001,STRUCTURE_NUMBER_008,LONG_017\n12,ABC,080130000\n";

        var ex = await Assert.ThrowsAsync<NbiFormatException>(() => ParseAsync(noLat));
        Assert.Contains("LAT_016", ex.Message);
    }

    [Fact]
    public async Task Field_count_mismatch_is_a_structural_fault_not_an_exception()
    {
        var rows = await ParseAsync(Header + "\n12,086,1,ABC,25470000,080130000,1972,6,SURPLUS\n");

        var row = Assert.Single(rows);
        Assert.Null(row.Record);
        Assert.Contains("Field count", row.StructuralFault);
        Assert.Equal(2, row.LineNumber);
    }

    [Fact]
    public async Task Blank_key_fields_are_a_structural_fault()
    {
        var rows = await ParseAsync(Header + "\n12,086,1,,25470000,080130000,1972,6\n");

        var row = Assert.Single(rows);
        Assert.Null(row.Record);
        Assert.Contains("key field", row.StructuralFault);
    }

    [Fact]
    public async Task Record_type_defaults_to_1_when_blank()
    {
        var rows = await ParseAsync(Header + "\n12,086,,ABC,25470000,080130000,1972,6\n");

        Assert.Equal("1", rows[0].Record!.RecordType);
    }

    [Fact]
    public async Task Blank_lines_are_skipped_and_raw_line_is_preserved()
    {
        var rows = await ParseAsync(Header + "\n12,086,1,ABC,25470000,080130000,1972,6\n\n");

        var row = Assert.Single(rows);
        Assert.Equal("12,086,1,ABC,25470000,080130000,1972,6", row.RawLine);
    }

    [Fact]
    public async Task Empty_file_throws_NbiFormatException()
    {
        await Assert.ThrowsAsync<NbiFormatException>(() => ParseAsync(""));
    }

    [Fact]
    public async Task Parses_the_committed_fixture_without_structural_surprises()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "nbi_sample_2025.csv");
        var parser = new NbiSnapshotParser();
        await using var stream = File.OpenRead(path);

        var total = 0;
        var structural = 0;
        await foreach (var row in parser.ParseAsync(stream))
        {
            total++;
            if (row.StructuralFault is not null)
            {
                structural++;
            }
        }

        Assert.Equal(114, total);
        Assert.Equal(2, structural); // the two deliberate structural-fault lines
    }
}
