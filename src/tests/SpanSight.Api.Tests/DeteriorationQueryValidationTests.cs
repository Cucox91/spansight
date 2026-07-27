using SpanSight.Api.Endpoints;
using SpanSight.Core.Analytics;
using SpanSight.Core.Domain.Lookups;

namespace SpanSight.Api.Tests;

/// <summary>
/// FR-1.3 — the matrix request rules, without a database.
/// <para>
/// The rule that matters most here is that the three cohort dimensions travel together. A row that is
/// the national sentinel in one dimension and a real group in another is neither a cohort nor the
/// national total, so a partial cohort must be a client error rather than something the API silently
/// completes (methodology §4).
/// </para>
/// </summary>
public class DeteriorationQueryValidationTests
{
    private const string Type = "Girder / Stringer";
    private const string Material = "Steel";
    private const string Region = "Northeast";

    [Theory]
    [InlineData("Deck", ConditionComponent.Deck)]
    [InlineData("deck", ConditionComponent.Deck)]
    [InlineData("SUPERSTRUCTURE", ConditionComponent.Superstructure)]
    [InlineData("Substructure", ConditionComponent.Substructure)]
    [InlineData("Culvert", ConditionComponent.Culvert)]
    public void A_component_is_accepted_in_any_casing(string input, ConditionComponent expected)
    {
        Assert.True(DeteriorationEndpoints.TryBuildQuery(input, null, null, null, out var query, out _));
        Assert.Equal(expected, query.Component);
    }

    /// <summary>Omitting all three cohort dimensions asks for the national all-cohorts context matrix.</summary>
    [Fact]
    public void Omitting_every_cohort_dimension_selects_the_national_matrix()
    {
        Assert.True(DeteriorationEndpoints.TryBuildQuery("Deck", null, null, null, out var query, out _));

        Assert.Equal(NbiCohorts.AllCohorts, query.TypeGroup);
        Assert.Equal(NbiCohorts.AllCohorts, query.MaterialGroup);
        Assert.Equal(NbiCohorts.AllCohorts, query.Region);
    }

    [Fact]
    public void A_full_cohort_is_normalised_to_its_canonical_spelling()
    {
        Assert.True(DeteriorationEndpoints.TryBuildQuery(
            "Deck", "girder / stringer", "STEEL", "northeast", out var query, out _));

        Assert.Equal(Type, query.TypeGroup);
        Assert.Equal(Material, query.MaterialGroup);
        Assert.Equal(Region, query.Region);
    }

    [Theory]
    [InlineData(Type, null, null)]
    [InlineData(null, Material, null)]
    [InlineData(null, null, Region)]
    [InlineData(Type, Material, null)]
    [InlineData(Type, null, Region)]
    [InlineData(null, Material, Region)]
    public void A_partial_cohort_is_refused(string? type, string? material, string? region)
    {
        Assert.False(DeteriorationEndpoints.TryBuildQuery("Deck", type, material, region, out _, out var errors));
        Assert.True(errors.ContainsKey("typeGroup"));
    }

    [Theory]
    [InlineData(null, "component")]
    [InlineData("", "component")]
    [InlineData("Girders", "component")]
    [InlineData("LowestRating", "component")]
    // Enum.TryParse accepts any numeric string and hands back an undefined enum value, so these
    // reached the database as a component that does not exist and came back 200 with a fabricated
    // family label over an empty grid.
    [InlineData("99", "component")]
    [InlineData("0", "component")]
    [InlineData("-3", "component")]
    [InlineData("2", "component")]
    public void A_missing_or_unknown_component_names_its_field(string? component, string field)
    {
        Assert.False(DeteriorationEndpoints.TryBuildQuery(component, null, null, null, out _, out var errors));
        Assert.True(errors.ContainsKey(field));
    }

    [Theory]
    [InlineData("Girder", Material, Region, "typeGroup")]
    [InlineData(Type, "Iron", Region, "materialGroup")]
    [InlineData(Type, Material, "Midwest", "region")]
    public void An_unknown_group_names_its_own_field(string type, string material, string region, string field)
    {
        Assert.False(DeteriorationEndpoints.TryBuildQuery("Deck", type, material, region, out _, out var errors));
        Assert.True(errors.ContainsKey(field));
    }

    /// <summary>
    /// The reserved sentinel is reached by omitting the dimensions, not by naming it — otherwise there
    /// would be two ways to ask for the national matrix and a client could build a half-sentinel row.
    /// </summary>
    [Fact]
    public void The_reserved_sentinel_cannot_be_requested_by_name()
    {
        Assert.False(DeteriorationEndpoints.TryBuildQuery(
            "Deck", NbiCohorts.AllCohorts, NbiCohorts.AllCohorts, NbiCohorts.AllCohorts, out _, out var errors));
        Assert.True(errors.ContainsKey("typeGroup"));
    }

    /// <summary>The labelled buckets are ordinary cohort values and must be requestable.</summary>
    [Fact]
    public void The_not_published_and_non_conus_buckets_are_selectable()
    {
        Assert.True(DeteriorationEndpoints.TryBuildQuery(
            "Culvert", NbiCohorts.NotPublished, NbiCohorts.NotPublished, NbiCohorts.OutsideContiguousUs,
            out var query, out _));

        Assert.Equal(NbiCohorts.NotPublished, query.TypeGroup);
        Assert.Equal(NbiCohorts.OutsideContiguousUs, query.Region);
    }

    /// <summary>
    /// GR-6 as an assertion rather than a convention: the copy the API ships with every matrix has to
    /// carry the framing, and must not carry predictive vocabulary. FR-1.3 is the most easily
    /// misread feature in the product, so the words are pinned.
    /// </summary>
    [Fact]
    public void The_method_note_states_the_gr6_framing()
    {
        var note = DeteriorationEndpoints.MethodNote(null);

        Assert.Contains("not engineering advice", note);
        Assert.Contains("not a prediction", note);
        Assert.Contains("cohort level", note);
    }

    /// <summary>
    /// The vintage range comes from the run, so a job published over part of the catalog cannot ship a
    /// note claiming the whole of it. Hardcoding "1992–2025" put that exact contradiction on the CI
    /// fixture, whose run is 2020–2023.
    /// </summary>
    [Fact]
    public void The_method_note_takes_its_vintage_range_from_the_run()
    {
        var run = new DeteriorationRun
        {
            JobRunId = "j",
            CatalogSha256 = "s",
            MethodologyVersion = "v1.1",
            FirstYear = 2020,
            LastYear = 2023,
        };

        Assert.Contains("2020–2023", DeteriorationEndpoints.MethodNote(run));
        Assert.DoesNotContain("1992", DeteriorationEndpoints.MethodNote(run));

        // With nothing published the note describes itself rather than naming a range it cannot know.
        Assert.DoesNotContain("–", DeteriorationEndpoints.MethodNote(null));
    }

    /// <summary>
    /// The share in the cadence caption is a rate, so a matrix with no above-floor row states no
    /// share at all rather than a number computed from evidence the response just suppressed.
    /// </summary>
    [Fact]
    public void The_suppressed_cadence_caption_carries_the_caveat_but_no_number()
    {
        Assert.DoesNotContain('%', DeteriorationEndpoints.CadenceCaptionWithoutShare);
        Assert.Contains("no share is stated", DeteriorationEndpoints.CadenceCaptionWithoutShare);
        Assert.Contains("24-month", DeteriorationEndpoints.CadenceCaptionWithoutShare);
        Assert.Contains("understated", DeteriorationEndpoints.CadenceCaptionWithoutShare);
    }

    [Theory]
    [InlineData("forecast")]
    [InlineData("projected")]
    [InlineData("expected life")]
    [InlineData("remaining life")]
    [InlineData("steady state")]
    [InlineData("will deteriorate")]
    [InlineData("likely to")]
    public void No_published_copy_promises_the_future(string forbidden)
    {
        foreach (var copy in PublishedCopy)
        {
            Assert.DoesNotContain(forbidden, copy, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Every user-visible string the API ships with a matrix.</summary>
    private static string[] PublishedCopy =>
    [
        DeteriorationEndpoints.MethodNote(null),
        DeteriorationEndpoints.CadenceCaptionTemplate,
        DeteriorationEndpoints.CadenceCaptionWithoutShare,
    ];

    /// <summary>
    /// "Predict" is the one word that has a legitimate use here — but only in the disclaimer. Every
    /// occurrence must be part of "not a prediction", so copy can deny predicting and never claim it.
    /// </summary>
    [Fact]
    public void Prediction_is_only_ever_mentioned_to_deny_it()
    {
        foreach (var copy in PublishedCopy)
        {
            var mentions = System.Text.RegularExpressions.Regex.Matches(copy, "predict", RegexOptions).Count;
            var denials = System.Text.RegularExpressions.Regex.Matches(copy, "not a prediction", RegexOptions).Count;
            Assert.Equal(denials, mentions);
        }
    }

    private const System.Text.RegularExpressions.RegexOptions RegexOptions =
        System.Text.RegularExpressions.RegexOptions.IgnoreCase;

    /// <summary>The §6.1 caveat is not optional decoration — it is the dominant feature of every matrix.</summary>
    [Fact]
    public void The_cadence_caption_explains_why_the_diagonal_dominates()
    {
        var caption = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            DeteriorationEndpoints.CadenceCaptionTemplate, "91.0%");

        Assert.Contains("91.0%", caption);
        Assert.Contains("above the sample-size floor", caption);
        Assert.Contains("24-month", caption);
        Assert.Contains("understate", caption);
    }

    [Fact]
    public void The_methodology_link_points_at_the_published_document() =>
        Assert.EndsWith("docs/METHODOLOGY-DETERIORATION.md", DeteriorationEndpoints.MethodologyUrl);
}
