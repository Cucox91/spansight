using SpanSight.Core.Domain.Lookups;

namespace SpanSight.Core.Tests.Analytics;

/// <summary>
/// FR-1.3 AC-1/AC-3 — golden tests over <c>tools/deterioration/deterioration.sql</c>.
/// <para>
/// Two halves, for two different risks. The <b>synthetic</b> tests run the job over a hand-written
/// vintage CSV whose every matrix cell was computed by hand first
/// (<c>src/tests/fixtures/deterioration/README.md</c>) — that is AC-3's "unit-tested against small
/// hand-computed fixtures", and it is the only way to reach a second climate region, the non-CONUS
/// bucket, the "Other" material group, the record-type filter and the duplicate-key tie-break, none
/// of which the Alabama-only era fixtures can produce.
/// </para>
/// <para>
/// The <b>era-fixture</b> tests then run the same published SQL over real published rows, and hold
/// its cohort assignment against <see cref="NbiCohorts"/> code by code. The cohort rule now exists in
/// SQL, in C# and in TypeScript; two of those already drifted apart without a test noticing, so this
/// is the FR-1.2 anti-drift pattern applied to the grouping instead of to Good/Fair/Poor.
/// </para>
/// </summary>
public class DeteriorationGoldenTests
{
    // ------------------------------------------------------------------ hand-computed (AC-3)

    /// <summary>
    /// The whole synthetic matrix, cell by cell, exactly as the fixture README states it. Every
    /// expected value here was worked out by hand from the CSV before the job was ever run over it.
    /// </summary>
    public static TheoryData<string, string, string, string, int, int, int> SyntheticCells => new()
    {
        // cohort (type / material / region), component, from, to, pairs
        { "Girder / Stringer", "Concrete", "Northeast", "Deck", 7, 7, 1 },
        { "Girder / Stringer", "Concrete", "Northeast", "Deck", 7, 6, 1 },
        { "Girder / Stringer", "Concrete", "Northeast", "Superstructure", 7, 6, 1 },
        { "Girder / Stringer", "Concrete", "Northeast", "Superstructure", 6, 6, 1 },
        // S01's substructure sits at 7 across all three vintages: two pairs in one cell.
        { "Girder / Stringer", "Concrete", "Northeast", "Substructure", 7, 7, 2 },

        // S02's rehabilitation, retained as observed (§3.3) — a decay model would have dropped it.
        { "Truss / Arch", "Steel", "Northeast", "Deck", 4, 8, 1 },
        // S12 changes to Concrete / Girder in year y+1; the pair belongs to the year-y cohort (§5).
        { "Truss / Arch", "Steel", "Northeast", "Deck", 6, 6, 1 },
        { "Truss / Arch", "Steel", "Northeast", "Superstructure", 5, 5, 1 },
        { "Truss / Arch", "Steel", "Northeast", "Superstructure", 6, 6, 1 },
        { "Truss / Arch", "Steel", "Northeast", "Substructure", 5, 5, 1 },
        { "Truss / Arch", "Steel", "Northeast", "Substructure", 6, 6, 1 },

        { "Girder / Stringer", "Prestressed concrete", "West", "Deck", 6, 5, 1 },
        // S11's duplicate identity key: the first published row wins, so this is 5->4 and not 9->8.
        { "Girder / Stringer", "Prestressed concrete", "West", "Deck", 5, 4, 1 },
        { "Girder / Stringer", "Prestressed concrete", "West", "Superstructure", 6, 6, 1 },
        { "Girder / Stringer", "Prestressed concrete", "West", "Superstructure", 5, 4, 1 },
        { "Girder / Stringer", "Prestressed concrete", "West", "Substructure", 6, 6, 1 },
        { "Girder / Stringer", "Prestressed concrete", "West", "Substructure", 5, 4, 1 },

        // Hawaii — the "Outside contiguous U.S." bucket, reached through the else branch of the map.
        { "Other", "Timber", "Outside contiguous U.S.", "Deck", 7, 7, 1 },
        { "Other", "Timber", "Outside contiguous U.S.", "Superstructure", 7, 7, 1 },
        { "Other", "Timber", "Outside contiguous U.S.", "Substructure", 7, 7, 1 },

        // Material code 9 (aluminium / iron) and code 0 both land in "Other" — different type groups
        // keep them separate cohorts, which is what proves the material grouping and not just a join.
        { "Truss / Arch", "Other", "South", "Deck", 5, 5, 1 },
        { "Truss / Arch", "Other", "South", "Superstructure", 5, 4, 1 },
        { "Truss / Arch", "Other", "South", "Substructure", 4, 4, 1 },
        { "Other", "Other", "South", "Deck", 3, 2, 1 },
        { "Other", "Other", "South", "Superstructure", 3, 3, 1 },
        { "Other", "Other", "South", "Substructure", 3, 3, 1 },

        // Items 43A and 43B absent — a labelled bucket, never folded into "Other" (§5).
        { "Not published", "Not published", "Northeast", "Deck", 6, 6, 1 },
        { "Not published", "Not published", "Northeast", "Superstructure", 6, 6, 1 },
        { "Not published", "Not published", "Northeast", "Substructure", 6, 6, 1 },

        // S14: items 58/59/60 are 'N', lowercase 'n' and blank; only item 62 pairs.
        { "Culvert", "Concrete", "Northeast", "Culvert", 6, 6, 1 },
    };

    [DuckDbOnlyTheory]
    [MemberData(nameof(SyntheticCells))]
    public void The_hand_computed_matrix_is_reproduced_cell_for_cell(
        string typeGroup, string materialGroup, string region, string component, int from, int to, int pairs)
    {
        var init = DuckDb.CreateSyntheticInitScript();
        try
        {
            Assert.Equal(pairs.ToString(), DuckDb.Run(
                $"""
                 SELECT pairs FROM det_matrix_cell
                 WHERE type_group = '{typeGroup}' AND material_group = '{materialGroup}'
                   AND region = '{region}' AND component = '{component}'
                   AND from_rating = {from} AND to_rating = {to}
                 """, init));
        }
        finally
        {
            File.Delete(init);
        }
    }

    /// <summary>
    /// The counts around the cells: exactly 30 non-zero cohort cells and nothing else. A cell the
    /// theory above does not name would slip past it, so the total is asserted separately.
    /// </summary>
    [DuckDbOnlyFact]
    public void The_synthetic_fixture_produces_exactly_the_cells_that_were_hand_computed()
    {
        var init = DuckDb.CreateSyntheticInitScript();
        try
        {
            var counts = DuckDb.Run(
                """
                SELECT
                  (SELECT count(*) FROM det_pair),
                  (SELECT count(*) FROM (SELECT DISTINCT state_code, structure_number, from_year FROM det_pair)),
                  (SELECT count(DISTINCT (type_group, material_group, region)) FROM det_pair),
                  (SELECT count(*) FROM det_matrix_row  WHERE type_group <> 'All'),
                  (SELECT count(*) FROM det_matrix_cell WHERE type_group <> 'All'),
                  (SELECT count(*) FROM det_matrix_row  WHERE type_group  = 'All'),
                  (SELECT count(*) FROM det_matrix_cell WHERE type_group  = 'All')
                """, init);

            // 31 component pairs · 11 structure pairs · 8 cohorts · 29 cohort rows · 30 cohort cells
            // · 15 national rows · 21 national cells (fixture README).
            Assert.Equal("31|11|8|29|30|15|21", counts);

            // ...and the theory above names every one of those 30 cohort cells, so none can hide.
            Assert.Equal(30, SyntheticCells.Count);
        }
        finally
        {
            File.Delete(init);
        }
    }

    /// <summary>
    /// The structures that must contribute <em>nothing</em>. Each is a different way for a pair to be
    /// wrong: a bridged gap, a component that changed families, a route record mistaken for a
    /// structure, and a structure published once. Counting any of them would inflate a published rate.
    /// </summary>
    [DuckDbOnlyTheory]
    [InlineData("S03", "a gap year — published 2000 and 2002, never bridged (§3.1)")]
    [InlineData("S04", "a culvert recoded as a deck structure — no component rated in both years (§3.2)")]
    [InlineData("S10", "record type '2' — a route, not the structure (§2)")]
    [InlineData("S13", "published in one vintage only")]
    public void Structures_that_cannot_form_a_pair_contribute_none(string structureNumber, string why)
    {
        var init = DuckDb.CreateSyntheticInitScript();
        try
        {
            Assert.Equal("0", DuckDb.Run(
                $"SELECT count(*) FROM det_pair WHERE structure_number = '{structureNumber}'", init));
            Assert.NotEmpty(why);
        }
        finally
        {
            File.Delete(init);
        }
    }

    /// <summary>
    /// The duplicate-key tie-break, asserted on the values rather than only on the count: the row
    /// published first wins, so S11's deck pair is 5→4. Reversing the ORDER BY in the SQL gives 9→8
    /// and fails here; keeping both rows gives two pairs and fails the count.
    /// </summary>
    [DuckDbOnlyFact]
    public void A_duplicate_identity_key_keeps_the_row_published_first()
    {
        var init = DuckDb.CreateSyntheticInitScript();
        try
        {
            Assert.Equal("1|5|4", DuckDb.Run(
                """
                SELECT count(*), min(from_rating), min(to_rating)
                FROM det_pair WHERE structure_number = 'S11' AND component = 'Deck'
                """, init));
        }
        finally
        {
            File.Delete(init);
        }
    }

    /// <summary>
    /// The cohort is read from year y. S12 is Steel/Truss in 2000 and Concrete/Girder in 2001, so its
    /// pair must sit in the Truss / Arch × Steel cohort. Keying on y+1 moves it and fails here.
    /// </summary>
    [DuckDbOnlyFact]
    public void A_pair_whose_cohort_changes_is_counted_once_under_the_from_year()
    {
        var init = DuckDb.CreateSyntheticInitScript();
        try
        {
            Assert.Equal("1|Truss / Arch|Steel", DuckDb.Run(
                """
                SELECT count(*), min(type_group), min(material_group)
                FROM det_pair WHERE structure_number = 'S12' AND component = 'Deck'
                """, init));
        }
        finally
        {
            File.Delete(init);
        }
    }

    /// <summary>
    /// Row totals and spans for the two rows that span more than one year-pair. All 33 year-pairs are
    /// pooled, so the span is the only thing standing between a pooled cell and a misleading label
    /// (§4, §6.8) — it has to be right at the row level, not the cohort level.
    /// </summary>
    [DuckDbOnlyFact]
    public void Row_spans_describe_the_year_pairs_that_actually_contributed()
    {
        var init = DuckDb.CreateSyntheticInitScript();
        try
        {
            // S01 is the only structure with two consecutive pairs, and only its deck and substructure
            // rows stay on the same from-rating across both.
            Assert.Equal("2|2000|2001|2", DuckDb.Run(
                """
                SELECT row_total, first_from_year, last_from_year, year_pairs_observed
                FROM det_matrix_row
                WHERE component = 'Deck' AND type_group = 'Girder / Stringer'
                  AND material_group = 'Concrete' AND region = 'Northeast' AND from_rating = 7
                """, init));

            // Its superstructure splits into two single-year rows, which is what makes the span a
            // property of the row rather than of the cohort.
            Assert.Equal("1|2001|2001|1", DuckDb.Run(
                """
                SELECT row_total, first_from_year, last_from_year, year_pairs_observed
                FROM det_matrix_row
                WHERE component = 'Superstructure' AND type_group = 'Girder / Stringer'
                  AND material_group = 'Concrete' AND region = 'Northeast' AND from_rating = 6
                """, init));
        }
        finally
        {
            File.Delete(init);
        }
    }

    // ------------------------------------------------------------------ anti-drift (AC-1)

    /// <summary>
    /// The load-bearing test. The job assigns cohorts in SQL; the API and the UI label them from
    /// <see cref="NbiCohorts"/>. This runs the job's own lookup relations and compares them to the C#
    /// on every published code, in both directions — so a code added to one side, moved between
    /// groups, or dropped, fails here with the offending code rather than silently splitting a cohort.
    /// </summary>
    [DuckDbOnlyFact]
    public void The_sql_cohort_lookups_match_the_core_lookups_code_for_code()
    {
        var init = DuckDb.CreateSyntheticInitScript();
        try
        {
            AssertLookupMatches("det_material_group", "code_043a", "material_group", NbiCohorts.MaterialGroupByCode, init);
            AssertLookupMatches("det_type_group", "code_043b", "type_group", NbiCohorts.TypeGroupByCode, init);
            AssertLookupMatches("det_climate_region", "state_fips", "region", NbiCohorts.RegionByStateFips, init);
        }
        finally
        {
            File.Delete(init);
        }
    }

    private static void AssertLookupMatches(
        string relation, string keyColumn, string valueColumn, IReadOnlyDictionary<string, string> csharp, string init)
    {
        var sql = DuckDb.Run($"SELECT {keyColumn}, {valueColumn} FROM {relation} ORDER BY 1", init)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('|'))
            .ToDictionary(f => f[0], f => f[1], StringComparer.Ordinal);

        // Both directions: a code only the SQL knows would silently create a cohort the UI cannot
        // label, and a code only the C# knows would leave a group the job never populates.
        Assert.Equal(csharp.Keys.OrderBy(k => k, StringComparer.Ordinal), sql.Keys.OrderBy(k => k, StringComparer.Ordinal));
        foreach (var (code, group) in csharp)
        {
            Assert.Equal(group, sql[code]);
        }
    }

    /// <summary>
    /// The published SQL over real published rows (the FR-1.2 pattern). Every invariant the build
    /// script refuses to write without must hold on the era fixtures too — including on a source with
    /// only one consecutive year-pair, which is the shape most likely to expose an off-by-one in the
    /// pairing join.
    /// </summary>
    [DuckDbTheory]
    [InlineData("det_check_unmapped")]
    [InlineData("det_check_pair_shape")]
    [InlineData("det_check_row_totals")]
    [InlineData("det_check_national_is_the_sum")]
    [InlineData("det_check_sentinel_is_whole")]
    [InlineData("det_check_reserved_value")]
    [InlineData("det_check_span")]
    public void The_era_fixtures_satisfy_every_published_invariant(string check)
    {
        var init = DuckDb.CreateDeteriorationFixtureInitScript();
        try
        {
            Assert.Equal("0", DuckDb.Run($"SELECT count(*) FROM {check};", init));
        }
        finally
        {
            File.Delete(init);
        }
    }

    /// <summary>
    /// The era fixtures hold exactly one consecutive vintage pair (2016→2017), and it is the only real
    /// above-floor cohort row available anywhere in the committed data. Pinning it means a change to
    /// the pairing rule shows up against published rows, not only against the synthetic file.
    /// </summary>
    [DuckDbFact]
    public void The_one_consecutive_era_fixture_pair_produces_the_expected_totals()
    {
        var init = DuckDb.CreateDeteriorationFixtureInitScript();
        try
        {
            Assert.Equal("662|183|183|183|113", DuckDb.Run(
                """
                SELECT count(*),
                       count(*) FILTER (component = 'Deck'),
                       count(*) FILTER (component = 'Superstructure'),
                       count(*) FILTER (component = 'Substructure'),
                       count(*) FILTER (component = 'Culvert')
                FROM det_pair
                """, init));

            // 1992 shares no structure numbers with 1993 anywhere in Alabama (item 8 was reformatted),
            // and the fixtures skip 2011-2015 and 2018-2024 entirely, so exactly one year-pair exists.
            Assert.Equal("2016|2016|1", DuckDb.Run(
                "SELECT min(from_year), max(from_year), count(DISTINCT from_year) FROM det_pair", init));

            // The single cohort row that clears n >= 50 in the committed data, and its three cells.
            Assert.Equal("59|2|56|1", DuckDb.Run(
                """
                SELECT
                  (SELECT row_total FROM det_matrix_row WHERE component = 'Culvert'
                     AND type_group = 'Culvert' AND material_group = 'Concrete'
                     AND region = 'Southeast' AND from_rating = 6),
                  (SELECT pairs FROM det_matrix_cell WHERE component = 'Culvert'
                     AND type_group = 'Culvert' AND material_group = 'Concrete'
                     AND region = 'Southeast' AND from_rating = 6 AND to_rating = 5),
                  (SELECT pairs FROM det_matrix_cell WHERE component = 'Culvert'
                     AND type_group = 'Culvert' AND material_group = 'Concrete'
                     AND region = 'Southeast' AND from_rating = 6 AND to_rating = 6),
                  (SELECT pairs FROM det_matrix_cell WHERE component = 'Culvert'
                     AND type_group = 'Culvert' AND material_group = 'Concrete'
                     AND region = 'Southeast' AND from_rating = 6 AND to_rating = 7)
                """, init));
        }
        finally
        {
            File.Delete(init);
        }
    }

    /// <summary>
    /// Both sides of the floor exist in the committed era fixtures — 1 cohort row above and 88 below,
    /// 7 national rows above and 21 below. The methodology's "the national matrix is always above
    /// floor" is true of the full 34-vintage build and false here, which is exactly why it must never
    /// be encoded as an invariant: an implementation that skipped the floor for national rows would
    /// still pass every other test.
    /// </summary>
    [DuckDbFact]
    public void The_era_fixtures_exercise_both_sides_of_the_sample_size_floor()
    {
        var init = DuckDb.CreateDeteriorationFixtureInitScript();
        try
        {
            Assert.Equal("1|88|7|21", DuckDb.Run(
                """
                SELECT
                  count(*) FILTER (type_group <> 'All' AND row_total >= (SELECT sample_floor FROM det_methodology)),
                  count(*) FILTER (type_group <> 'All' AND row_total <  (SELECT sample_floor FROM det_methodology)),
                  count(*) FILTER (type_group  = 'All' AND row_total >= (SELECT sample_floor FROM det_methodology)),
                  count(*) FILTER (type_group  = 'All' AND row_total <  (SELECT sample_floor FROM det_methodology))
                FROM det_matrix_row
                """, init));
        }
        finally
        {
            File.Delete(init);
        }
    }

    /// <summary>
    /// The published floor exists once, in the SQL, and flows outward through the manifest and the run
    /// row. If the methodology ever changes it, this fails and points at every place that has to move
    /// together — including the fixture manifest and the doc.
    /// </summary>
    [DuckDbOnlyFact]
    public void The_sample_floor_and_methodology_version_are_the_published_ones()
    {
        var init = DuckDb.CreateSyntheticInitScript();
        try
        {
            Assert.Equal("50|v1.1|All|Not published", DuckDb.Run(
                "SELECT sample_floor, methodology_version, all_cohorts, not_published FROM det_methodology", init));
        }
        finally
        {
            File.Delete(init);
        }
    }
}
