using SpanSight.Core.Vintages;

namespace SpanSight.Core.Tests.Vintages;

/// <summary>
/// Era quirks, superset normalization and reject codes (FR-1.1 AC-1/AC-2). Header lines here are
/// trimmed-down but structurally real: the column names and the era signatures are the ones the
/// published FHWA files actually carry.
/// </summary>
public class VintageNormalizationTests
{
    // Minimal headers that still classify: identity + the signature column of each era.
    private const string TenYearRuleHeader =
        "STATE_CODE_001,STRUCTURE_NUMBER_008,DECK_COND_058,SUFFICIENCY_RATING,STATUS_WITH_10YR_RULE";
    private const string SufficiencyHeader =
        "STATE_CODE_001,STRUCTURE_NUMBER_008,DECK_COND_058,SUFFICIENCY_RATING,STATUS";
    private const string PerformanceHeader =
        "STATE_CODE_001,STRUCTURE_NUMBER_008,DECK_COND_058,BRIDGE_CONDITION,DECK_AREA";

    [Theory]
    [InlineData(TenYearRuleHeader, VintageEra.TenYearRule)]
    [InlineData(SufficiencyHeader, VintageEra.SufficiencyRating)]
    [InlineData(PerformanceHeader, VintageEra.PerformanceMeasures)]
    public void Classifies_era_from_signature_columns(string header, VintageEra expected) =>
        Assert.Equal(expected, VintageEraClassifier.Classify(header.Split(',')));

    [Fact]
    public void TenYearRule_era_wins_over_sufficiency_when_both_signatures_present()
    {
        // 1992 carries SUFFICIENCY_RATING too, so the narrower signature has to be tested first.
        Assert.Equal(VintageEra.TenYearRule, VintageEraClassifier.Classify(TenYearRuleHeader.Split(',')));
    }

    [Fact]
    public void Unrecognisable_header_is_not_an_era() =>
        Assert.Null(VintageEraClassifier.Classify("id,name,value".Split(',')));

    [Fact]
    public void Wrong_era_for_the_declared_year_fails_loudly()
    {
        // The acceptance case: a 1992-era file handed to the 2025 converter path must not produce rows.
        var ex = Assert.Throws<VintageFormatException>(() => VintageHeader.Bind(TenYearRuleHeader, 2025));

        Assert.Contains("2025", ex.Message, StringComparison.Ordinal);
        Assert.Contains("10-year-rule era", ex.Message, StringComparison.Ordinal);
        Assert.Contains("nothing was converted", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Matching_era_for_a_pinned_year_binds()
    {
        var header = VintageHeader.Bind(TenYearRuleHeader, 1992);
        Assert.Equal(VintageEra.TenYearRule, header.Era);
    }

    [Fact]
    public void Every_published_vintage_year_is_pinned_to_an_era()
    {
        // After the W2 full run there are no unpinned years left: all 34 were read from the real files.
        var unpinned = Enumerable.Range(1992, 2025 - 1992 + 1).Where(y => VintageYearEra.Expected(y) is null).ToList();

        Assert.Empty(unpinned);
        Assert.Equal(34, VintageYearEra.PinnedYears.Count);
    }

    [Theory]
    // The boundaries that a naive "10-year rule ended in year N" cutoff would get wrong.
    [InlineData(1992, VintageEra.TenYearRule)]
    [InlineData(2009, VintageEra.TenYearRule)]
    [InlineData(2010, VintageEra.SufficiencyRating)]   // the 10-yr-rule columns vanish…
    [InlineData(2011, VintageEra.SufficiencyRating)]
    [InlineData(2012, VintageEra.TenYearRule)]         // …and come back for seven more vintages
    [InlineData(2018, VintageEra.TenYearRule)]
    [InlineData(2019, VintageEra.PerformanceMeasures)]
    [InlineData(2025, VintageEra.PerformanceMeasures)]
    public void Pinned_era_sequence_is_not_monotonic(int year, VintageEra expected) =>
        Assert.Equal(expected, VintageYearEra.Expected(year));

    [Fact]
    public void A_year_pinned_to_another_era_is_refused_even_mid_series()
    {
        // 2012 looks like it "should" be the sufficiency era by year alone; the file says otherwise.
        var ex = Assert.Throws<VintageFormatException>(() => VintageHeader.Bind(SufficiencyHeader, 2012));

        Assert.Contains("nothing was converted", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_2016_to_2018_cat_columns_bind_as_themselves()
    {
        // 2017/2018 append CAT10/CAT23/CAT29. They are the 2019+ trio under FHWA's older opaque
        // names, but the Parquet stays a faithful copy: they bind as themselves and are NOT
        // renamed into BRIDGE_CONDITION/LOWEST_RATING/DECK_AREA. catalog.sql does the coalescing.
        var header = VintageHeader.Bind(TenYearRuleHeader + ",CAT10,CAT23,CAT29", 2017);
        var row = VintageConverter.Normalize("01,ABC123,7,88.5,1,G,7,250.0", 2, header).Values!;

        Assert.Equal("G", row[IndexOf("CAT10")]);
        Assert.Equal("7", row[IndexOf("CAT23")]);
        Assert.Equal("250.0", row[IndexOf("CAT29")]);

        // The readable names stay NULL for this era — that gap is exactly why catalog.sql coalesces.
        Assert.Null(row[IndexOf("BRIDGE_CONDITION")]);
        Assert.Null(row[IndexOf("LOWEST_RATING")]);
        Assert.Null(row[IndexOf("DECK_AREA")]);
    }

    [Fact]
    public void Every_cat_column_declares_the_successor_it_is_the_predecessor_of()
    {
        // Guards the mapping catalog.sql relies on: if a CAT column is added or renamed here
        // without a successor, the coalesced 2016-2025 series would silently lose a vintage.
        foreach (var (cat, successor) in VintageSchema.CatColumnSuccessors)
        {
            Assert.Contains(cat, VintageSchema.Columns);
            Assert.Contains(successor, VintageSchema.Columns);
        }

        Assert.Equal(3, VintageSchema.CatColumnSuccessors.Count);
    }

    [Fact]
    public void Unknown_column_fails_loudly_rather_than_being_dropped()
    {
        var ex = Assert.Throws<VintageFormatException>(
            () => VintageHeader.Bind(PerformanceHeader + ",BRAND_NEW_ITEM_999", 2025));

        Assert.Contains("BRAND_NEW_ITEM_999", ex.Message, StringComparison.Ordinal);
        Assert.Contains("deliberate schema change", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_identity_column_fails_loudly()
    {
        var ex = Assert.Throws<VintageFormatException>(
            () => VintageHeader.Bind("STATE_CODE_001,DECK_COND_058,BRIDGE_CONDITION", 2025));

        Assert.Contains(VintageSchema.StructureNumber, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_column_one_era_lacks_normalizes_to_null_in_the_same_position()
    {
        // The superset promise: identical columns in identical order whichever era produced the row.
        var older = VintageHeader.Bind(SufficiencyHeader, 2010);
        var newer = VintageHeader.Bind(PerformanceHeader, 2025);

        var olderRow = VintageConverter.Normalize("01,ABC123,7,88.5,A", 2, older).Values!;
        var newerRow = VintageConverter.Normalize("01,ABC123,7,G,250.0", 2, newer).Values!;

        var bridgeCondition = IndexOf("BRIDGE_CONDITION");
        var sufficiency = IndexOf("SUFFICIENCY_RATING");

        Assert.Null(olderRow[bridgeCondition]);          // absent in the older era
        Assert.Equal("G", newerRow[bridgeCondition]);
        Assert.Equal("88.5", olderRow[sufficiency]);
        Assert.Null(newerRow[sufficiency]);              // dropped by the newer era

        Assert.Equal(olderRow.Length, newerRow.Length);
        Assert.Equal(VintageSchema.Columns.Count, olderRow.Length);
    }

    [Fact]
    public void Absent_columns_are_reported_for_the_manifest()
    {
        var header = VintageHeader.Bind(SufficiencyHeader, 2010);

        Assert.Contains("BRIDGE_CONDITION", header.AbsentColumns);
        Assert.DoesNotContain("SUFFICIENCY_RATING", header.AbsentColumns);
    }

    [Fact]
    public void Field_count_mismatch_is_rejected_with_a_reason_not_silently_reshaped()
    {
        var header = VintageHeader.Bind(PerformanceHeader, 2025);
        var result = VintageConverter.Normalize("01,ABC123,7", 42, header);

        Assert.Null(result.Values);
        Assert.Equal(VintageRejectReasons.FieldCountMismatch, result.RejectReason);
        Assert.Equal(42, result.LineNumber);
        Assert.Contains("got 3 fields", result.RejectDetail!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(",ABC123,7,G,250.0")]   // blank state
    [InlineData("01,,7,G,250.0")]       // blank structure number
    [InlineData("01,   ,7,G,250.0")]    // whitespace-only structure number
    public void Blank_identity_is_rejected_with_a_reason(string line)
    {
        var header = VintageHeader.Bind(PerformanceHeader, 2025);
        var result = VintageConverter.Normalize(line, 7, header);

        Assert.Null(result.Values);
        Assert.Equal(VintageRejectReasons.MissingKeyField, result.RejectReason);
    }

    [Fact]
    public void Empty_field_becomes_null_so_absent_and_blank_read_the_same_downstream()
    {
        var header = VintageHeader.Bind(PerformanceHeader, 2025);
        var values = VintageConverter.Normalize("01,ABC123,,G,250.0", 2, header).Values!;

        Assert.Null(values[IndexOf("DECK_COND_058")]);
    }

    [Fact]
    public void Condition_code_N_is_preserved_and_never_confused_with_missing()
    {
        // Culvert records carry 'N' for items 58-60; the classifier ignores it, but it is a
        // published value and losing it would change what the data says.
        var header = VintageHeader.Bind(PerformanceHeader, 2025);
        var values = VintageConverter.Normalize("01,ABC123,N,G,250.0", 2, header).Values!;

        Assert.Equal("N", values[IndexOf("DECK_COND_058")]);
    }

    private static int IndexOf(string column)
    {
        for (var i = 0; i < VintageSchema.Columns.Count; i++)
        {
            if (string.Equals(VintageSchema.Columns[i], column, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        throw new InvalidOperationException($"{column} not in the superset schema.");
    }
}

/// <summary>The published dialect is violated by the published data; these pin what we do about it.</summary>
public class VintageLineTests
{
    [Fact]
    public void Unescaped_apostrophe_inside_a_qualified_field_does_not_break_the_row()
    {
        // Real 1992/2010 values: 'O'NEAL ROAD', 'MOORE'S MILL CREEK'. Treating ' as a quote
        // character mis-splits these — DuckDB's sniffer refuses the 1992 file for exactly this.
        var fields = VintageLine.Split("01,ABC,'O'NEAL ROAD','MOORE'S MILL CREEK',7");

        Assert.Equal(5, fields.Length);
        Assert.Equal("O'NEAL ROAD", fields[2]);
        Assert.Equal("MOORE'S MILL CREEK", fields[3]);
    }

    [Theory]
    [InlineData("'BUCK CREEK              '", "BUCK CREEK")]  // older eras pad inside the qualifier
    [InlineData("'PERDIDO CREEK'", "PERDIDO CREEK")]          // 2025 does not
    [InlineData("  7  ", "7")]
    [InlineData("''", "")]
    [InlineData("'", "'")]                                     // a lone quote is data, not a pair
    [InlineData("", "")]
    public void Qualifier_and_padding_are_stripped_so_eras_compare_equal(string raw, string expected) =>
        Assert.Equal(expected, VintageLine.Clean(raw));

    [Fact]
    public void Field_count_is_preserved_for_empty_trailing_fields() =>
        Assert.Equal(4, VintageLine.Split("01,ABC,,").Length);
}
