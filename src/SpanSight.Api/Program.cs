using System.Threading.RateLimiting;

using Azure.Core;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;

using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;

using Npgsql;

using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

using Scalar.AspNetCore;

using SpanSight.Api;
using SpanSight.Api.Endpoints;
using SpanSight.Core.Ai;
using SpanSight.Core.Data;

var builder = WebApplication.CreateBuilder(args);

// Cloud DB auth is Entra-only (ADR-006-B: password auth disabled on the server); locally the
// compose connection string carries a password and this flag stays off.
if (builder.Configuration.GetValue<bool>("Database:UseEntraToken"))
{
    var dataSourceBuilder = new NpgsqlDataSourceBuilder(builder.Configuration.GetConnectionString("SpanSight"));
    dataSourceBuilder.UseNetTopologySuite();
    var credential = new DefaultAzureCredential();
    dataSourceBuilder.UsePeriodicPasswordProvider(
        async (_, cancellationToken) => (await credential.GetTokenAsync(
            new TokenRequestContext(["https://ossrdbms-aad.database.windows.net/.default"]), cancellationToken)).Token,
        successRefreshInterval: TimeSpan.FromMinutes(45),
        failureRefreshInterval: TimeSpan.FromSeconds(10));
    var dataSource = dataSourceBuilder.Build();
    builder.Services.AddDbContext<SpanSightDbContext>(options =>
        options.UseNpgsql(dataSource, npgsql => npgsql.UseNetTopologySuite()));
}
else
{
    builder.Services.AddDbContext<SpanSightDbContext>(options =>
        options.UseNpgsql(
            builder.Configuration.GetConnectionString("SpanSight"),
            npgsql => npgsql.UseNetTopologySuite()));
}

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection(AiOptions.SectionName));

builder.Services.AddHealthChecks()
    .AddCheck<DbReadyHealthCheck>("database", tags: ["ready"]);

// Public read-only API: no auth in P0–P2, so rate limiting + strict CORS carry the abuse story
// (ARCHITECTURE §7). Fixed window per client IP; the AI surface gets a much tighter policy.
builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue("RateLimiting:PermitLimit", 100),
                Window = TimeSpan.FromSeconds(builder.Configuration.GetValue("RateLimiting:WindowSeconds", 10)),
            }));
    limiter.AddPolicy("ai", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});

var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(cors => cors.AddDefaultPolicy(policy =>
    policy.WithOrigins(corsOrigins).AllowAnyHeader().WithMethods("GET", "POST")));

// OTel is config-gated (ADR-006-B): local compose sets Otlp:Endpoint (collector on 4318);
// cloud sets APPLICATIONINSIGHTS_CONNECTION_STRING and traces flow to App Insights (NFR-6).
// Neither present → no exporter, zero overhead.
var otlpEndpoint = builder.Configuration["Otlp:Endpoint"];
if (!string.IsNullOrEmpty(otlpEndpoint))
{
    builder.Services.AddOpenTelemetry()
        .WithTracing(tracing => tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddNpgsql()
            .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint)))
        .WithMetrics(metrics => metrics
            .AddAspNetCoreInstrumentation()
            .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint)));
}
else if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry()
        .UseAzureMonitor()
        .WithTracing(tracing => tracing.AddNpgsql());
}

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();
app.UseRateLimiter();

// Security headers for a public, read-only JSON API + the OpenAPI UI.
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});

app.MapOpenApi();
app.MapScalarApiReference(options => options.WithTitle("SpanSight API"));
app.MapGet("/swagger", () => Results.Redirect("/scalar/v1")).ExcludeFromDescription();

app.MapHealthChecks("/healthz", new HealthCheckOptions { Predicate = _ => false }); // liveness
app.MapHealthChecks("/readyz", new HealthCheckOptions { Predicate = r => r.Tags.Contains("ready") });

// The AI group gets the tight policy; everything else uses the global limiter only.
var apiGroup = app.MapGroup("/api");
apiGroup.MapBridges();
apiGroup.MapStats();
apiGroup.MapLookups();
apiGroup.MapQa();
apiGroup.MapGroup("").RequireRateLimiting("ai").MapAi();

app.Run();

/// <summary>Exposed for WebApplicationFactory-based integration tests.</summary>
public partial class Program;
