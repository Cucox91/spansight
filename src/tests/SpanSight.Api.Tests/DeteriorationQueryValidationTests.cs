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
        Assert.Contains("not engineering advice", DeteriorationEndpoints.MethodNote);
        Assert.Contains("not a prediction", DeteriorationEndpoints.MethodNote);
        Assert.Contains("cohort level", DeteriorationEndpoints.MethodNote);
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
        foreach (var copy in (ReadOnlySpan<string>)
                 [DeteriorationEndpoints.MethodNote, DeteriorationEndpoints.CadenceCaptionTemplate])
        {
            Assert.DoesNotContain(forbidden, copy, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// "Predict" is the one word that has a legitimate use here — but only in the disclaimer. Every
    /// occurrence must be part of "not a prediction", so copy can deny predicting and never claim it.
    /// </summary>
    [Fact]
    public void Prediction_is_only_ever_mentioned_to_deny_it()
    {
        foreach (var copy in (ReadOnlySpan<string>)
                 [DeteriorationEndpoints.MethodNote, DeteriorationEndpoints.CadenceCaptionTemplate])
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
        Assert.Contains("24-month", caption);
        Assert.Contains("understate", caption);
    }

    [Fact]
    public void The_methodology_link_points_at_the_published_document() =>
        Assert.EndsWith("docs/METHODOLOGY-DETERIORATION.md", DeteriorationEndpoints.MethodologyUrl);
}
