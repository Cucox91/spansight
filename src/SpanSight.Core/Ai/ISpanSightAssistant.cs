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
/// ("Showing: Poor condition · Truss / Arch · Florida"). <see cref="Applied"/> is the validated
/// rail-shaped predicate the SPA applies directly; <see cref="Filter"/> is the same predicate in
/// API form. The interpretation is rendered deterministically from validated values — never
/// model-authored text (GR-6, ADR-008 §2).
/// </summary>
public sealed record NlFilterResult(BridgeFilter Filter, NlFilterSpec Applied, string Interpretation);
