using SpanSight.Core.Filtering;

namespace SpanSight.Core.Ai;

/// <summary>
/// Port for the Phase 0.5 AI-assist features (SRS FR-AI.1–3, ADR-008). Providers adapt behind
/// this interface in the API host; the core contract is deliberately narrow — the model can only
/// produce values the validated <see cref="BridgeFilter"/> could already express (ADR-008 §2).
/// </summary>
public interface ISpanSightAssistant
{
    /// <summary>
    /// FR-AI.1: translate a plain-English request into the existing filter predicate.
    /// User text is data, never instructions (ADR-008 §3).
    /// </summary>
    Task<NlFilterResult> TranslateQueryAsync(string text, CancellationToken cancellationToken = default);
}

/// <summary>
/// The translated filter plus the human-readable interpretation the UI shows for correction
/// ("Showing: Poor-condition steel bridges in FL built before 1970").
/// </summary>
public sealed record NlFilterResult(BridgeFilter Filter, string Interpretation);
