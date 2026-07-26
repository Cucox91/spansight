using SpanSight.Core.Analytics;
using SpanSight.Core.Domain;

namespace SpanSight.Core.Tests.Analytics;

/// <summary>
/// The packed per-bridge series encoding (FR-1.2 AC-1). These are the assertions that let the
/// serving tables store 34 years in one row without the API having to guess what a character means.
/// </summary>
public class ConditionSeriesCodecTests
{
    [Fact]
    public void Decode_yields_one_observation_per_published_year()
    {
        var observations = ConditionSeriesCodec.Decode(2010, "4.....44.......4");

        Assert.Equal([2010, 2016, 2017, 2025], observations.Select(o => o.Year));
        Assert.All(observations, o => Assert.Equal(4, o.LowestRating));
        Assert.All(observations, o => Assert.Equal(ConditionClass.Poor, o.ConditionClass));
    }

    [Fact]
    public void Gap_years_are_omitted_rather_than_returned_as_nulls()
    {
        // The distinction that GR-6 turns on: a year FHWA did not publish must not arrive at the UI
        // as a data point at all, because anything drawn there would be invented.
        var observations = ConditionSeriesCodec.Decode(2020, "7..7");

        Assert.Equal(2, observations.Count);
        Assert.DoesNotContain(observations, o => o.Year is 2021 or 2022);
    }

    [Fact]
    public void An_unrated_year_is_an_observation_with_no_rating_not_a_gap()
    {
        // 'U' is a real published state — a record with 'N' in items 58-60 and 62 — and is very
        // different from the structure being absent that year.
        var observations = ConditionSeriesCodec.Decode(1992, "U");

        var only = Assert.Single(observations);
        Assert.Equal(1992, only.Year);
        Assert.Null(only.LowestRating);
        Assert.Equal(ConditionClass.Unknown, only.ConditionClass);
    }

    [Theory]
    [InlineData('9', ConditionClass.Good)]
    [InlineData('7', ConditionClass.Good)]
    [InlineData('6', ConditionClass.Fair)]
    [InlineData('5', ConditionClass.Fair)]
    [InlineData('4', ConditionClass.Poor)]
    [InlineData('0', ConditionClass.Poor)]
    public void Class_comes_from_the_phase_0_classifier_at_every_threshold(char mark, ConditionClass expected)
    {
        var only = Assert.Single(ConditionSeriesCodec.Decode(2025, mark.ToString()));

        Assert.Equal(expected, only.ConditionClass);
        // Decoding must agree with the classifier by construction, not by a copied table.
        Assert.Equal(ConditionClassifier.Classify(mark - '0'), only.ConditionClass);
    }

    [Fact]
    public void Encode_and_decode_round_trip_including_gaps_and_unrated_years()
    {
        const string packed = "78.6U..5";

        var decoded = ConditionSeriesCodec.Decode(2018, packed);
        var reencoded = ConditionSeriesCodec.Encode(2018, 2025, decoded);

        Assert.Equal(packed, reencoded);
    }

    [Fact]
    public void Encode_fills_unobserved_years_in_the_span_with_gaps()
    {
        var packed = ConditionSeriesCodec.Encode(2020, 2024,
        [
            new ConditionObservation(2020, 8, ConditionClass.Good),
            new ConditionObservation(2024, 5, ConditionClass.Fair),
        ]);

        Assert.Equal("8...5", packed);
    }

    [Fact]
    public void Encode_refuses_an_observation_outside_the_span()
    {
        // Silently dropping it would produce a series whose observed count no longer matches, and
        // that mismatch would surface much later as an unexplainable chart.
        Assert.Throws<ArgumentOutOfRangeException>(() => ConditionSeriesCodec.Encode(2020, 2022,
            [new ConditionObservation(2025, 7, ConditionClass.Good)]));
    }

    [Theory]
    [InlineData(2010, 2025, "4.....44.......4", true)]
    [InlineData(1992, 1992, "U", true)]
    [InlineData(2010, 2025, "4.....44......4", false)]     // one short of the span
    [InlineData(2010, 2012, ".78", false)]                 // leading gap — bounds are wrong
    [InlineData(2010, 2012, "78.", false)]                 // trailing gap — bounds are wrong
    [InlineData(2010, 2012, "7X8", false)]                 // not in the alphabet
    [InlineData(2010, 2012, "", false)]
    [InlineData(2012, 2010, "7", false)]                   // inverted span
    public void IsWellFormed_accepts_only_series_the_api_can_explain(
        int firstYear, int lastYear, string ratings, bool expected) =>
        Assert.Equal(expected, ConditionSeriesCodec.IsWellFormed(firstYear, lastYear, ratings));

    [Fact]
    public void CountObserved_counts_published_years_not_span_length() =>
        Assert.Equal(4, ConditionSeriesCodec.CountObserved("4.....44.......4"));

    [Fact]
    public void A_full_34_year_series_fits_the_stored_column()
    {
        var packed = ConditionSeriesCodec.Encode(1992, 2025,
            Enumerable.Range(1992, 34).Select(y => new ConditionObservation(y, 7, ConditionClass.Good)));

        Assert.Equal(34, packed.Length);
        Assert.True(packed.Length <= ConditionSeriesCodec.MaxLength);
        Assert.Equal(34, ConditionSeriesCodec.Decode(1992, packed).Count);
    }
}
