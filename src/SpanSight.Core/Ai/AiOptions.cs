namespace SpanSight.Core.Ai;

/// <summary>
/// Configuration for the Phase 0.5 AI-assist features. Disabled by default (ADR-008 §4);
/// stays off until the FR-AI acceptance criteria are elaborated and met (SDLC §3). The API key
/// is never configured here — user-secrets locally, Container Apps secrets in cloud (NFR-4).
/// </summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary>Master switch. Off ⇒ /api/ai/* returns 503 feature-disabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>Provider id for the adapter registration (first target: "anthropic").</summary>
    public string Provider { get; set; } = "anthropic";

    /// <summary>Model id, chosen for cost (Haiku-class); pinned at implementation time per ADR-008.</summary>
    public string Model { get; set; } = "";

    /// <summary>Hard daily request ceiling — the cost governor that trips the feature off (ADR-008 §4).</summary>
    public int MaxRequestsPerDay { get; set; } = 200;

    /// <summary>Per-request output token cap.</summary>
    public int MaxOutputTokens { get; set; } = 512;
}
