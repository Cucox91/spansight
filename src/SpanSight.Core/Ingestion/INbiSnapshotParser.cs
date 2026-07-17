namespace SpanSight.Core.Ingestion;

/// <summary>
/// Parses a raw FHWA NBI delimited snapshot (1992–2025 Coding Guide era) into typed records.
/// </summary>
/// <remarks>
/// Contract (FR-0.1/FR-0.2): the parser is a pure stream transform — it never throws on a bad
/// <em>row</em> (bad rows come back as <see cref="NbiRowResult.StructuralFault"/> for the
/// quarantine channel) and throws <see cref="NbiFormatException"/> only when the <em>file</em>
/// is unusable (missing required header columns, empty file).
/// </remarks>
public interface INbiSnapshotParser
{
    IAsyncEnumerable<NbiRowResult> ParseAsync(Stream snapshot, CancellationToken cancellationToken = default);
}
