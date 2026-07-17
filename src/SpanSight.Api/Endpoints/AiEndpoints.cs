using Microsoft.Extensions.Options;

using SpanSight.Core.Ai;

namespace SpanSight.Api.Endpoints;

/// <summary>
/// Phase 0.5 surface (FR-AI.1, ADR-008). Scaffolded now, dark by default: with
/// <c>Ai:Enabled=false</c> (or no provider registered) every route answers 503 with a
/// ProblemDetails body, so the front end can ship its affordance behind the same flag.
/// </summary>
public static class AiEndpoints
{
    public static RouteGroupBuilder MapAi(this RouteGroupBuilder group)
    {
        group.MapPost("/ai/query", TranslateAsync)
            .WithSummary("Natural-language query → filters (Phase 0.5)")
            .WithDescription(
                "Translates a plain-English request into the same validated filter predicate the filter rail uses. " +
                "Guardrails per ADR-008: schema-constrained output, user text is data not instructions, " +
                "translation only — never engineering judgment (GR-6).");

        return group;
    }

    private static async Task<IResult> TranslateAsync(
        NlQueryRequestDto request,
        IOptions<AiOptions> options,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["text"] = ["text is required."],
            });
        }

        var assistant = services.GetService<ISpanSightAssistant>();
        if (!options.Value.Enabled || assistant is null)
        {
            return TypedResults.Problem(
                title: "AI assist is not enabled",
                detail: "Phase 0.5 feature (FR-AI.1) — ships after the Phase 0 gate. See docs/ARCHITECTURE.md ADR-008.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var result = await assistant.TranslateQueryAsync(request.Text.Trim(), cancellationToken);
        return TypedResults.Ok(result);
    }
}
