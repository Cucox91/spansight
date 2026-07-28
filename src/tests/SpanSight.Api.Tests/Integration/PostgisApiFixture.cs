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

    /// <summary>FR-1.2 — the hand-written trend fixture, loaded through the real trend pipeline.</summary>
    public TrendLoadSummary TrendSeedSummary { get; private set; } = null!;

    /// <summary>FR-1.3 — the hand-written matrix fixture, loaded through the real deterioration pipeline.</summary>
    public DeteriorationLoadSummary DeteriorationSeedSummary { get; private set; } = null!;

    /// <summary>FR-1.5 — the hand-placed county-join fixture, loaded through the real join pipeline.</summary>
    public CountyJoinLoadSummary CountyJoinSeedSummary { get; private set; } = null!;

    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        ConnectionString = _postgres.GetConnectionString();

        SeedSummary = await LoadFixtureAsync(force: false);
        TrendSeedSummary = await LoadTrendsAsync(force: false);
        DeteriorationSeedSummary = await LoadDeteriorationAsync(force: false);
        CountyJoinSeedSummary = await LoadCountyJoinAsync(force: false);

        _factory = new WebApplicationFactory<ApiAssemblyMarker>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:SpanSight", ConnectionString);
            builder.UseSetting("Otlp:Endpoint", "");
            // The whole collection shares one client and therefore one rate-limiter partition, and
            // the production default is 100 requests per 10 seconds. The suite crossed that when
            // FR-1.4 arrived, and the symptom is every test in the collection failing with 429 at
            // once — which reads like a broken endpoint, not a limiter. Raised here so the limit is
            // a deployment concern; that a route is inside the limited group is proved directly by
            // RankingIntegrationTests, against its own host with a limit of two.
            builder.UseSetting("RateLimiting:PermitLimit", "10000");
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

    /// <summary>
    /// Publishes the committed trend fixture through the production loader (FR-1.2), so the API
    /// tests read rows that went through the same upsert and convergence path as a real publish.
    /// </summary>
    public async Task<TrendLoadSummary> LoadTrendsAsync(bool force, string? directory = null)
    {
        await using var db = NewDbContext();
        var pipeline = new TrendLoadPipeline(db, NullLogger<TrendLoadPipeline>.Instance);
        return await pipeline.RunAsync(
            directory ?? Path.Combine(AppContext.BaseDirectory, "fixtures", "trends"),
            dryRun: false,
            force: force);
    }

    /// <summary>
    /// Publishes the committed matrix fixture through the production loader (FR-1.3), so the API tests
    /// read rows that went through the same upsert and convergence path as a real publish.
    /// </summary>
    public async Task<DeteriorationLoadSummary> LoadDeteriorationAsync(bool force, string? directory = null)
    {
        await using var db = NewDbContext();
        var pipeline = new DeteriorationLoadPipeline(db, NullLogger<DeteriorationLoadPipeline>.Instance);
        return await pipeline.RunAsync(
            directory ?? Path.Combine(AppContext.BaseDirectory, "fixtures", "deterioration-aggregates"),
            dryRun: false,
            force: force);
    }

    /// <summary>
    /// Publishes the committed county-join fixture through the production loader (FR-1.5), so the
    /// QA and report-card tests read rows that went through the same upsert, reconciliation and
    /// convergence path as a real publish.
    /// </summary>
    public async Task<CountyJoinLoadSummary> LoadCountyJoinAsync(bool force, string? directory = null)
    {
        await using var db = NewDbContext();
        var pipeline = new CountyJoinLoadPipeline(db, NullLogger<CountyJoinLoadPipeline>.Instance);
        return await pipeline.RunAsync(
            directory ?? Path.Combine(AppContext.BaseDirectory, "fixtures", "census-join-aggregates"),
            dryRun: false,
            force: force);
    }

    public SpanSightDbContext NewDbContext() => new(
        new DbContextOptionsBuilder<SpanSightDbContext>()
            .UseNpgsql(ConnectionString, o => o.UseNetTopologySuite())
            .Options);

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
