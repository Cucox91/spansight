using SpanSight.Core.Domain;
using SpanSight.Core.Domain.Lookups;

namespace SpanSight.Api.Endpoints;

public static class LookupEndpoints
{
    public static RouteGroupBuilder MapLookups(this RouteGroupBuilder group)
    {
        group.MapGet("/lookups", () => TypedResults.Ok(Lookups))
            .WithSummary("Code tables")
            .WithDescription("States and the published NBI code→label tables the UI decodes with (items 43A/43B, condition ratings).");

        return group;
    }

    private static readonly LookupsDto Lookups = new(
        StateFips.ByFips.Values
            .OrderBy(s => s.Abbreviation)
            .Select(s => new LookupsDto.StateDto(s.Fips, s.Abbreviation, s.Name))
            .ToList(),
        NbiCodes.Materials,
        NbiCodes.Designs,
        NbiCodes.ConditionRatings,
        [nameof(ConditionClass.Good), nameof(ConditionClass.Fair), nameof(ConditionClass.Poor), nameof(ConditionClass.Unknown)]);
}
