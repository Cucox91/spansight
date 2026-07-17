using Microsoft.Extensions.Diagnostics.HealthChecks;

using SpanSight.Core.Data;

namespace SpanSight.Api;

/// <summary>Readiness = the serving database answers. Liveness (/healthz) never touches the DB.</summary>
public sealed class DbReadyHealthCheck(SpanSightDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("database reachable")
                : HealthCheckResult.Unhealthy("database unreachable");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("database unreachable", ex);
        }
    }
}
