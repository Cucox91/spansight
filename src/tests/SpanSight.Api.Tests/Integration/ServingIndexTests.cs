using Microsoft.EntityFrameworkCore;

namespace SpanSight.Api.Tests.Integration;

/// <summary>
/// NFR-1: the indexes the served query shapes depend on exist in the migrated schema.
/// <para>
/// This is the only kind of NFR-1 regression the suite can catch. A missing index changes no
/// response — every functional test still passes, every number is still right — and the only
/// symptom is that a shape that took a millisecond takes fifty, on a machine nobody is watching.
/// The perf pass (<c>tools/perf/perf-pass.sh</c>) is what measures; this is what stops the thing it
/// measured from quietly going away between passes.
/// </para>
/// <para>
/// It deliberately asserts that the index <em>exists</em>, not that a plan uses it. The fixture
/// holds 114 rows, and Postgres is right to sequentially scan 114 rows however many indexes are
/// available — so a plan assertion here would either be vacuous or would pin a plan that is wrong
/// at national scale. What the plan looks like at 741k rows is in the perf report, measured.
/// </para>
/// </summary>
[Collection("postgis-api")]
public class ServingIndexTests(PostgisApiFixture fixture)
{
    /// <summary>
    /// Each entry is an index and the reason it is on the hot path, so a future reader deciding
    /// whether to drop one is reading the cost of dropping it rather than guessing.
    /// </summary>
    public static TheoryData<string, string, string> ServingIndexes => new()
    {
        {
            "core", "ix_bridge_ranking",
            "FR-1.4: makes every ranking grouping an index-only scan over the record-type-1 rows. " +
            "Measured 40 ms against 74 ms on the parallel sequential scan it replaced."
        },
        {
            "core", "IX_bridge_snapshot_year",
            "FR-1.4: every ranking and report card asks which snapshot it is serving before it does " +
            "anything else. Unindexed that ORDER BY … DESC LIMIT 1 is a full scan of 741k rows — " +
            "measured at 38-53 ms, the single largest cost in both endpoints, against 0.07 ms indexed."
        },
        {
            "analytics", "IX_bridge_condition_series_state_code_structure_number",
            "FR-1.2: the drawer's history fetch, covering, so one structure's series is one index read."
        },
        {
            "analytics", "IX_condition_rollup_level_fips_vintage_year",
            "FR-1.2: the trend series for a state or county, already ordered by year."
        },
        {
            // Trailing '~' is not a typo: EF truncates generated index names at Postgres's 63-byte
            // identifier limit and marks the cut. The name in the database is this one.
            "analytics", "IX_deterioration_matrix_row_component_type_group_material_grou~",
            "FR-1.3: one cohort's matrix rows for one component."
        },
        {
            "analytics", "IX_census_county_county_fips",
            "FR-1.5: the county name and ACS population on every report card."
        },
    };

    [DockerTheory]
    [MemberData(nameof(ServingIndexes))]
    public async Task Serving_index_exists(string schema, string index, string why)
    {
        await using var db = fixture.NewDbContext();

        var present = await db.Database
            .SqlQuery<int>($"""
                select 1 as "Value" from pg_indexes
                where schemaname = {schema} and indexname = {index}
                """)
            .AnyAsync();

        Assert.True(present, $"Index {schema}.{index} is missing from the migrated schema. {why}");
    }
}
