using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using SpanSight.Core.Data;
using SpanSight.Core.Domain.Lookups;

namespace SpanSight.Ingestion;

/// <summary>
/// Streams <c>core.bridge</c> as newline-delimited GeoJSON features (GeoJSONSeq) for the
/// tippecanoe tile build (FR-0.5, tools/build-tiles.sh). Properties are the lean set the map
/// styles and filters on client-side (ADR-002) — detail comes from the API, not the tiles.
/// </summary>
public sealed class GeoJsonExporter(SpanSightDbContext db, ILogger<GeoJsonExporter> logger)
{
    public async Task<int> ExportAsync(string outputPath, CancellationToken cancellationToken = default)
    {
        await using var file = File.Create(outputPath);
        await using var writer = new StreamWriter(file);

        var count = 0;
        await foreach (var b in db.Bridges.AsNoTracking().OrderBy(b => b.Id).AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            var feature = new
            {
                type = "Feature",
                geometry = new { type = "Point", coordinates = new[] { b.Location.X, b.Location.Y } },
                properties = new
                {
                    id = $"{StateFips.ByFips[b.StateCode].Abbreviation}-{b.StructureNumber}",
                    state = StateFips.ByFips[b.StateCode].Abbreviation,
                    county = b.CountyCode,
                    cond = b.ConditionClass.ToString(),
                    lowest = b.LowestRating,
                    year = b.YearBuilt,
                    adt = b.Adt,
                    material = b.MaterialCode,
                    design = b.DesignCode,
                },
            };

            await writer.WriteLineAsync(JsonSerializer.Serialize(feature));
            count++;
        }

        logger.LogInformation("Exported {Count:N0} features to {Path}.", count, outputPath);

        await WriteMetaAsync(outputPath, count, cancellationToken);
        return count;
    }

    /// <summary>
    /// Sidecar for the tile manifest (FR-0.5 AC-3): ties the export — and therefore the PMTiles
    /// artifact built from it — to the ingestion run that produced the data.
    /// </summary>
    private async Task WriteMetaAsync(string outputPath, int featureCount, CancellationToken cancellationToken)
    {
        var run = await db.IngestionRuns.AsNoTracking()
            .Where(r => r.Status == IngestionRunStatus.Completed)
            .OrderByDescending(r => r.CompletedUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var meta = new
        {
            generatedUtc = DateTimeOffset.UtcNow,
            featureCount,
            ingestionRun = run is null ? null : new
            {
                id = run.Id,
                snapshotYear = run.SnapshotYear,
                sourceFile = run.SourceFile,
                sourceSha256 = run.SourceSha256,
                completedUtc = run.CompletedUtc,
                rowsLoaded = run.RowsLoaded,
            },
        };

        var metaPath = outputPath + ".meta.json";
        await File.WriteAllTextAsync(metaPath, JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        logger.LogInformation("Export meta written to {Path}.", metaPath);
    }
}
