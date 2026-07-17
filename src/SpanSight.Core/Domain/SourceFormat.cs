namespace SpanSight.Core.Domain;

/// <summary>
/// Provenance of a canonical record (ARCHITECTURE §4.1): the schema must serve both
/// Coding Guide-era (1992–2025) and SNBI/NBI NextGen (2026+) vintages behind one model.
/// </summary>
public enum SourceFormat
{
    /// <summary>Legacy FHWA Recording &amp; Coding Guide delimited snapshot (1992–2025).</summary>
    LegacyCodingGuide = 1,

    /// <summary>SNBI / NBI NextGen submittal mapped through the FHWA crosswalk (2026+, Phase 1+).</summary>
    Snbi = 2,
}
