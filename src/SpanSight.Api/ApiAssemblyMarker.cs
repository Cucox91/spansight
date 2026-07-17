namespace SpanSight.Api;

/// <summary>
/// Anchor type for <c>WebApplicationFactory&lt;TEntryPoint&gt;</c>. Both the API and the
/// ingestion CLI expose a top-level-statements <c>Program</c> in the global namespace, so tests
/// that reference both projects need an unambiguous type from this assembly.
/// </summary>
public sealed class ApiAssemblyMarker;
