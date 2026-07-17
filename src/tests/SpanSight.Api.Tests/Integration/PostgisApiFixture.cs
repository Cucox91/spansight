using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using SpanSight.Core.Data;
using SpanSight.Core.Geo;
using SpanSight.Core.Ingestion;
using SpanSight.Core.Ingestion.Validation;
using SpanSight.Ingestion;

using Testcontainers.PostgreSql;

namespace SpanSight.Api.Tests.Integration;

/// <summary>
/// One PostGIS container + one migrated schema + the committed fixture loaded through the real
/// pipeline (not hand-inserted rows) — the API tests then exercise exactly what production runs.
/// Shared per collection to keep the suite fast.
/// </summary>
public sealed class PostgisApiFixture : IAsyncLifetime
{
    // Same multi-arch PostGIS image as docker-compose.yml.
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("imresamu/postgis:16-3.5")
        .WithDatabase("spansight")
        .WithUsername("spansight")
        .WithPassword("spansight")
        .Build();

    private WebApplicationFactory<ApiAssemblyMarker>? _factory;

    public string ConnectionString { get; private set; } = "";

    public LoadSummary SeedSummary { get; private set; } = null!;

    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        ConnectionString = _postgres.GetConnectionString();

        SeedSummary = await LoadFixtureAsync(force: false);

        _factory = new WebApplicationFactory<ApiAssemblyMarker>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:SpanSight", ConnectionString);
            builder.UseSetting("Otlp:Endpoint", "");
        });
        Client = _factory.CreateClient();
    }

    /// <summary>Runs the production load pipeline against the container (used for idempotency tests too).</summary>
    public async Task<LoadSummary> LoadFixtureAsync(bool force)
    {
        var options = new DbContextOptionsBuilder<SpanSightDbContext>()
            .UseNpgsql(ConnectionString, o => o.UseNetTopologySuite())
            .Options;
        await using var db = new SpanSightDbContext(options);

        var pipeline = new LoadPipeline(
            db,
            new NbiSnapshotParser(),
            new BridgeRowValidator(new NbiDmsCoordinateConverter()),
            NullLogger<LoadPipeline>.Instance);

        var fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "nbi_sample_2025.csv");
        return await pipeline.RunAsync(fixture, snapshotYear: 2025, dryRun: false, force: force, limit: null);
    }

    public async Task<int> CountBridgesAsync()
    {
        var options = new DbContextOptionsBuilder<SpanSightDbContext>()
            .UseNpgsql(ConnectionString, o => o.UseNetTopologySuite())
            .Options;
        await using var db = new SpanSightDbContext(options);
        return await db.Bridges.CountAsync();
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await _postgres.DisposeAsync();
    }
}

[CollectionDefinition("postgis-api")]
public sealed class PostgisApiCollection : ICollectionFixture<PostgisApiFixture>;
