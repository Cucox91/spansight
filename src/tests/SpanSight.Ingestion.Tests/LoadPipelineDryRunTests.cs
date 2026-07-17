using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using SpanSight.Core.Data;
using SpanSight.Core.Geo;
using SpanSight.Core.Ingestion;
using SpanSight.Core.Ingestion.Validation;

namespace SpanSight.Ingestion.Tests;

/// <summary>
/// FR-0.1 dry-run over the committed fixture: parse + validate the whole file with zero database
/// writes and assert the exact expected split. The fixture generator (tools/dev/make_fixture.py)
/// and these numbers move together — a drift here means the fixture changed without review.
/// </summary>
public class LoadPipelineDryRunTests
{
    private static LoadPipeline CreatePipeline()
    {
        // Dry-run never touches the connection; a context wired at an unreachable host proves it.
        var options = new DbContextOptionsBuilder<SpanSightDbContext>()
            .UseNpgsql("Host=localhost;Port=1;Database=never;Username=never;Password=never",
                o => o.UseNetTopologySuite())
            .Options;

        return new LoadPipeline(
            new SpanSightDbContext(options),
            new NbiSnapshotParser(),
            new BridgeRowValidator(new NbiDmsCoordinateConverter()),
            NullLogger<LoadPipeline>.Instance);
    }

    private static string FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "nbi_sample_2025.csv");

    [Fact]
    public async Task Dry_run_reports_exact_fixture_split_and_writes_nothing()
    {
        var pipeline = CreatePipeline();

        var summary = await pipeline.RunAsync(FixturePath, snapshotYear: 2025, dryRun: true, force: false, limit: null);

        Assert.False(summary.Skipped);
        Assert.Null(summary.RunId);
        Assert.Equal(114, summary.RowsRead);
        Assert.Equal(99, summary.RowsLoaded); // 98 generated + 1 quoted-comma row
        Assert.Equal(15, summary.RowsQuarantined); // 12 validation + 1 duplicate + 2 structural
    }

    [Fact]
    public async Task Limit_stops_early()
    {
        var pipeline = CreatePipeline();

        var summary = await pipeline.RunAsync(FixturePath, snapshotYear: 2025, dryRun: true, force: false, limit: 10);

        Assert.Equal(10, summary.RowsRead);
    }
}
