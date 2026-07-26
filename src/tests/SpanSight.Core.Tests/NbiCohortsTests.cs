using System.Text.RegularExpressions;

using SpanSight.Core.Ai;
using SpanSight.Core.Domain.Lookups;

namespace SpanSight.Core.Tests;

/// <summary>
/// FR-1.3 §5 — the cohort dimensions as pure lookups.
/// <para>
/// The committed era fixtures are Alabama only, so a fixture run can reach exactly one of the ten
/// climate buckets. Totality and disjointness therefore have to be asserted here, over the whole
/// lookup, rather than inferred from a job run that never touches nine of them.
/// </para>
/// </summary>
public class NbiCohortsTests
{
    // ------------------------------------------------------------------ climate regions

    /// <summary>
    /// The nine NOAA regions must cover the 48 contiguous states plus DC exactly once. A state in two
    /// regions double-counts its pairs; a state in none loses them. Either breaks the invariant that
    /// the cohort matrices sum to the national matrix.
    /// </summary>
    [Fact]
    public void The_nine_regions_cover_the_contiguous_states_and_dc_exactly_once()
    {
        var members = NbiCohorts.StatesByRegion.SelectMany(r => r.Value).ToList();

        Assert.Equal(9, NbiCohorts.StatesByRegion.Count);
        Assert.Equal(49, members.Count);
        Assert.Equal(49, members.Distinct(StringComparer.Ordinal).Count());

        // Derived from StateFips rather than retyped, so this cannot pass by agreeing with a second
        // copy of the same list: the contiguous 48 + DC are everything except AK, HI and the
        // territories.
        var expected = StateFips.ByFips.Values
            .Select(s => s.Abbreviation)
            .Except(["AK", "HI", "AS", "GU", "MP", "PR", "VI"], StringComparer.Ordinal)
            .OrderBy(a => a, StringComparer.Ordinal);

        Assert.Equal(expected, members.OrderBy(a => a, StringComparer.Ordinal));
    }

    /// <summary>
    /// Every jurisdiction SpanSight knows lands in exactly one bucket, and the non-CONUS bucket is the
    /// else branch rather than a fixed list — American Samoa and the Northern Marianas have never
    /// appeared in a published vintage, and must still resolve the first year one does.
    /// </summary>
    [Fact]
    public void Every_known_state_code_resolves_to_exactly_one_bucket()
    {
        Assert.Equal(StateFips.ByFips.Count, NbiCohorts.RegionByStateFips.Count);

        foreach (var (fips, state) in StateFips.ByFips)
        {
            var region = NbiCohorts.Region(fips);
            Assert.NotNull(region);
            Assert.Contains(region, NbiCohorts.Regions);
        }

        Assert.Equal(
            ["AK", "AS", "GU", "HI", "MP", "PR", "VI"],
            StateFips.ByFips
                .Where(s => NbiCohorts.Region(s.Key) == NbiCohorts.OutsideContiguousUs)
                .Select(s => s.Value.Abbreviation)
                .OrderBy(a => a, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("01", "Southeast")]
    [InlineData("11", "Northeast")]     // DC, folded into Northeast by NOAA's own convention
    [InlineData("29", "Ohio Valley")]   // Missouri is Ohio Valley, not South — an easy one to get wrong
    [InlineData("06", "West")]
    [InlineData("02", "Outside contiguous U.S.")]
    [InlineData("72", "Outside contiguous U.S.")]
    public void Named_states_land_where_the_methodology_says(string fips, string region) =>
        Assert.Equal(region, NbiCohorts.Region(fips));

    /// <summary>
    /// A state code SpanSight cannot resolve returns null so the job fails rather than inventing a
    /// bucket. There is no "not published" reading of a bridge with no state.
    /// </summary>
    [Theory]
    [InlineData("99")]
    [InlineData("00")]
    [InlineData("")]
    [InlineData(null)]
    public void An_unknown_state_code_has_no_region(string? fips) => Assert.Null(NbiCohorts.Region(fips));

    // ------------------------------------------------------------------ material and type groups

    /// <summary>Every published item-43A code is grouped, and no code is in two groups.</summary>
    [Fact]
    public void Material_groups_partition_every_published_43a_code()
    {
        Assert.Equal(
            NbiCodes.Materials.Keys.OrderBy(c => c, StringComparer.Ordinal),
            NbiCohorts.MaterialGroupByCode.Keys.OrderBy(c => c, StringComparer.Ordinal));

        Assert.Equal(
            NbiCohorts.MaterialCodesByGroup.Sum(g => g.Value.Length),
            NbiCohorts.MaterialGroupByCode.Count);
    }

    /// <summary>Every published item-43B code is grouped, and no code is in two groups.</summary>
    [Fact]
    public void Type_groups_partition_every_published_43b_code()
    {
        Assert.Equal(
            NbiCodes.Designs.Keys.OrderBy(c => c, StringComparer.Ordinal),
            NbiCohorts.TypeGroupByCode.Keys.OrderBy(c => c, StringComparer.Ordinal));

        Assert.Equal(
            NbiCohorts.TypeCodesByGroup.Sum(g => g.Value.Length),
            NbiCohorts.TypeGroupByCode.Count);
    }

    /// <summary>
    /// Code 9 is "Aluminum, wrought iron, or cast iron" in the Phase 0 decode and sits in the group the
    /// methodology names "Other". Pinning it keeps the decision visible: the group label is §5's, and
    /// the reason it is not misleading is that every view renders a group's member codes beside it.
    /// </summary>
    [Fact]
    public void Material_code_9_is_grouped_with_other_and_keeps_its_published_decode()
    {
        Assert.Equal("Other", NbiCohorts.MaterialGroup("9"));
        Assert.Equal("Other", NbiCohorts.MaterialGroup("0"));
        Assert.Equal("Aluminum, wrought iron, or cast iron", NbiCodes.DecodeMaterial("9"));
    }

    /// <summary>
    /// Blank or absent is a labelled bucket, not a group and not a drop. A present-but-unrecognised
    /// code returns null, so the job stops rather than filing an unknown published code under "Other".
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_absent_code_is_not_published_rather_than_other(string? code)
    {
        Assert.Equal(NbiCohorts.NotPublished, NbiCohorts.MaterialGroup(code));
        Assert.Equal(NbiCohorts.NotPublished, NbiCohorts.TypeGroup(code));
    }

    [Fact]
    public void An_unrecognised_published_code_resolves_to_nothing()
    {
        Assert.Null(NbiCohorts.MaterialGroup("A"));
        Assert.Null(NbiCohorts.TypeGroup("99"));
    }

    /// <summary>
    /// Item 43B is published zero-padded ('01', not '1') in all 34 vintages, but the lookup pads
    /// anyway — and padding must not turn an absent code into '00', which is the real published code
    /// for "Other".
    /// </summary>
    [Fact]
    public void Padding_a_type_code_never_manufactures_the_other_group()
    {
        Assert.Equal("Girder / Stringer", NbiCohorts.TypeGroup("1"));
        Assert.Equal("Girder / Stringer", NbiCohorts.TypeGroup("01"));
        Assert.Equal("Other", NbiCohorts.TypeGroup("00"));
        Assert.Equal(NbiCohorts.NotPublished, NbiCohorts.TypeGroup(""));
    }

    // ------------------------------------------------------------------ the reserved sentinel

    /// <summary>
    /// The national context matrix lives in the same tables as every cohort, marked by the reserved
    /// value in all three dimensions. If a published group were ever spelled the same way, the
    /// national matrix and a real cohort would collide.
    /// </summary>
    [Fact]
    public void No_published_group_collides_with_the_national_sentinel() =>
        Assert.False(NbiCohorts.ReservedValueCollidesWithAGroup);

    // ------------------------------------------------------------------ C# / TypeScript parity

    /// <summary>
    /// The item-43B grouping exists in C# and in <c>web/src/state/filters.ts</c>, where the SPA needs
    /// it for the filter rail. Until FR-1.3 the two were held together only by a comment; a third
    /// reader (the DuckDB job) made that untenable, so parity is asserted.
    /// </summary>
    [Fact]
    public void The_typescript_filter_rail_groups_match_the_core_lookup()
    {
        var source = File.ReadAllText(RepoPath.From("web", "src", "state", "filters.ts"));
        var block = Regex.Match(source, @"export const TYPE_GROUPS = \{(?<body>.*?)\n\} as const", RegexOptions.Singleline);
        Assert.True(block.Success, "TYPE_GROUPS was not found in web/src/state/filters.ts in the expected shape.");

        var typescript = Regex.Matches(block.Groups["body"].Value, @"'?(?<name>[^':\n]+?)'?\s*:\s*\[(?<codes>[^\]]*)\]")
            .ToDictionary(
                m => m.Groups["name"].Value.Trim(),
                m => Regex.Matches(m.Groups["codes"].Value, @"'(?<code>[^']+)'")
                    .Select(c => c.Groups["code"].Value)
                    .OrderBy(c => c, StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);

        Assert.Equal(
            NbiCohorts.TypeCodesByGroup.Keys.OrderBy(k => k, StringComparer.Ordinal),
            typescript.Keys.OrderBy(k => k, StringComparer.Ordinal));

        foreach (var (group, codes) in NbiCohorts.TypeCodesByGroup)
        {
            Assert.Equal(codes.OrderBy(c => c, StringComparer.Ordinal), typescript[group]);
        }
    }

    /// <summary>
    /// The FR-AI.1 translator now projects the same lookup instead of holding a second copy, and it
    /// must stay case-insensitive because model output arrives with unpredictable casing.
    /// </summary>
    [Fact]
    public void The_ai_translator_projects_the_same_groups_case_insensitively()
    {
        Assert.Equal(
            NbiCohorts.TypeCodesByGroup.Keys.OrderBy(k => k, StringComparer.Ordinal),
            NlTypeGroups.DesignCodesByGroup.Keys.OrderBy(k => k, StringComparer.Ordinal));

        Assert.True(NlTypeGroups.DesignCodesByGroup.TryGetValue("truss / arch", out var codes));
        Assert.Equal(NbiCohorts.TypeCodesByGroup["Truss / Arch"], codes);
    }
}
