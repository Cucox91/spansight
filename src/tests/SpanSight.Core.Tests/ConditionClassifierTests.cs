using SpanSight.Core.Domain;

using Xunit;

namespace SpanSight.Core.Tests;

public class ConditionClassifierTests
{
    [Theory]
    [InlineData("7", "7", "7", "N", 7)]
    [InlineData("6", "5", "5", "N", 5)]
    [InlineData("4", "6", "6", "N", 4)]
    [InlineData("N", "N", "N", "6", 6)] // culvert record: class comes from item 62
    [InlineData("0", "9", "9", "N", 0)]
    [InlineData("9", null, null, null, 9)]
    public void Lowest_rating_is_min_of_numeric_codes(string? deck, string? sup, string? sub, string? culvert, int expected)
    {
        Assert.Equal(expected, ConditionClassifier.LowestRating(deck, sup, sub, culvert));
    }

    [Theory]
    [InlineData("N", "N", "N", "N")]
    [InlineData(null, null, null, null)]
    [InlineData("N", "", " ", null)]
    public void No_numeric_rating_returns_null(string? deck, string? sup, string? sub, string? culvert)
    {
        Assert.Null(ConditionClassifier.LowestRating(deck, sup, sub, culvert));
    }

    [Theory]
    [InlineData(9, ConditionClass.Good)]
    [InlineData(7, ConditionClass.Good)]
    [InlineData(6, ConditionClass.Fair)]
    [InlineData(5, ConditionClass.Fair)]
    [InlineData(4, ConditionClass.Poor)]
    [InlineData(0, ConditionClass.Poor)]
    [InlineData(null, ConditionClass.Unknown)]
    public void Classify_follows_fhwa_thresholds(int? lowest, ConditionClass expected)
    {
        Assert.Equal(expected, ConditionClassifier.Classify(lowest));
    }
}
