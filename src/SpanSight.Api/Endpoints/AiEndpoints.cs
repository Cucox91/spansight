using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

using SpanSight.Api.Ai;
using SpanSight.Core.Ai;

namespace SpanSight.Api.Endpoints;

/// <summary>
/// FR-AI.1 surface (ADR-008). Dark by default: with <c>Ai:Enabled=false</c> (or no provider
/// registered) every route answers 503 with a ProblemDetails body, so the front end can ship its
/// affordance behind the same flag. When enabled: a daily request budget trips the feature to
/// "temporarily unavailable" (§4), responses are cached on normalized input, and provider
/// failures never surface model text — only ProblemDetails.
/// </summary>
public static class AiEndpoints
{
    public static RouteGroupBuilder MapAi(this RouteGroupBuilder group)
    {
        group.MapPost("/ai/query", TranslateAsync)
            .WithSummary("Natural-language query → filters (FR-AI.1)")
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
        IMemoryCache cache,
        AiRequestBudget budget,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 500)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["text"] = ["text is required and must be at most 500 characters."],
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

        // Cache on normalized input so repeats are free (ADR-008 §4). In-memory for the
        // single-replica demo; Redis takes over when the Phase 2 sidecar exists.
        var key = string.Join(' ',
            request.Text.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (cache.TryGetValue<NlQueryResponseDto>(("ai-query", key), out var cached) && cached is not null)
        {
            return TypedResults.Ok(cached);
        }

        if (!budget.TryConsume())
        {
            return TypedResults.Problem(
                title: "AI assist is temporarily unavailable",
                detail: "The daily request budget is exhausted (ADR-008 cost governor). Filters still work by hand.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        try
        {
            var result = await assistant.TranslateQueryAsync(request.Text.Trim(), cancellationToken);
            var response = NlQueryResponseDto.From(result);
            cache.Set(("ai-query", key), response, TimeSpan.FromHours(24));
            return TypedResults.Ok(response);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            loggerFactory.CreateLogger("SpanSight.Api.Ai").LogError(ex, "NL query translation failed.");
            return TypedResults.Problem(
                title: "AI assist is temporarily unavailable",
                detail: "The translation provider did not return a usable result. Filters still work by hand.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
