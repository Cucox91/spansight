// SpanSight.Ingestion — CLI worker: parse → validate → load → export (IMPLEMENTATION-PLAN §2).
// Runs locally/on demand; never in the cloud (ARCHITECTURE §3 — outputs are the published artifacts).

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using SpanSight.Core.Data;
using SpanSight.Core.Geo;
using SpanSight.Core.Ingestion;
using SpanSight.Core.Ingestion.Validation;
using SpanSight.Ingestion;

var (options, parseError) = CliOptions.Parse(args);
if (options is null)
{
    Console.Error.WriteLine(parseError);
    Console.Error.WriteLine();
    Console.Error.WriteLine(CliOptions.Usage);
    return 2;
}

// Content root = binary directory so appsettings.json resolves no matter where the CLI is invoked from.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory });
builder.Logging.SetMinimumLevel(LogLevel.Information);

var connectionString = options.Connection
    ?? builder.Configuration.GetConnectionString("SpanSight")
    ?? throw new InvalidOperationException("No connection string: pass --connection or set ConnectionStrings:SpanSight.");

builder.Services.AddDbContext<SpanSightDbContext>(o =>
    o.UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite()));
builder.Services.AddSingleton<IDmsCoordinateConverter, NbiDmsCoordinateConverter>();
builder.Services.AddSingleton<INbiSnapshotParser, NbiSnapshotParser>();
builder.Services.AddSingleton<BridgeRowValidator>();
builder.Services.AddScoped<LoadPipeline>();
builder.Services.AddScoped<GeoJsonExporter>();

using var host = builder.Build();
await using var scope = host.Services.CreateAsyncScope();
var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("SpanSight.Ingestion");

try
{
    switch (options.Command)
    {
        case "load":
            {
                var pipeline = scope.ServiceProvider.GetRequiredService<LoadPipeline>();
                var summary = await pipeline.RunAsync(
                    options.File!, options.SnapshotYear!.Value, options.DryRun, options.Force, options.Limit);
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(summary));
                return 0;
            }

        case "export-geojson":
            {
                var exporter = scope.ServiceProvider.GetRequiredService<GeoJsonExporter>();
                await exporter.ExportAsync(options.Output!);
                return 0;
            }

        case "migrate":
            {
                var db = scope.ServiceProvider.GetRequiredService<SpanSightDbContext>();
                await db.Database.MigrateAsync();
                logger.LogInformation("Migrations applied.");
                return 0;
            }

        default:
            Console.Error.WriteLine(CliOptions.Usage);
            return 2;
    }
}
catch (NbiFormatException ex)
{
    logger.LogError("Snapshot rejected: {Message}", ex.Message);
    return 1;
}
catch (Exception ex)
{
    logger.LogError(ex, "Ingestion failed.");
    return 1;
}

/// <summary>Marker for test discovery; the CLI entry point stays top-level statements.</summary>
public partial class Program;
