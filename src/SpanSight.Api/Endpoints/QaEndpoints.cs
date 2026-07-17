using Microsoft.EntityFrameworkCore;

using SpanSight.Core.Data;
using SpanSight.Core.Domain.Lookups;

namespace SpanSight.Api.Endpoints;

public static class QaEndpoints
{
    public static RouteGroupBuilder MapQa(this RouteGroupBuilder group)
    {
        group.MapGet("/qa/summary", SummaryAsync)
            .WithSummary("Data-quality report")
            .WithDescription("Latest ingestion run with reject breakdown by reason code and by state (FR-0.2, UC-0.6). Figures reconcile with the run summary by construction — same tables.");

        return group;
    }

    private static async Task<IResult> SummaryAsync(SpanSightDbContext db, CancellationToken cancellationToken)
    {
        var latest = await db.IngestionRuns.AsNoTracking()
            .Where(r => r.Status == IngestionRunStatus.Completed)
            .OrderByDescending(r => r.StartedUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (latest is null)
        {
            return TypedResults.Ok(new QaSummaryDto(null, [], []));
        }

        // Reasons live in a Postgres text[]; unnest is clearer as SQL than as a LINQ contortion.
        var byReason = await db.Database
            .SqlQuery<ReasonRow>($"""
                SELECT unnest(reasons) AS reason, count(*)::int AS count
                FROM quarantine.quarantine_row
                WHERE ingestion_run_id = {latest.Id}
                GROUP BY 1
                ORDER BY 2 DESC
                """)
            .ToListAsync(cancellationToken);

        var byState = await db.QuarantineRows.AsNoTracking()
            .Where(q => q.IngestionRunId == latest.Id && q.StateCode != null)
            .GroupBy(q => q.StateCode!)
            .Select(g => new { StateCode = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .Take(15)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new QaSummaryDto(
            new IngestionRunDto(
                latest.Id, latest.SourceFile, latest.SnapshotYear, latest.StartedUtc, latest.CompletedUtc,
                latest.Status.ToString(), latest.RowsRead, latest.RowsLoaded, latest.RowsQuarantined,
                latest.RowsRead == 0 ? 0 : Math.Round((double)latest.RowsQuarantined / latest.RowsRead, 4)),
            byReason.Select(r => new ReasonCountDto(r.Reason, r.Count)).ToList(),
            byState.Select(s => new StateCountDto(
                s.StateCode,
                StateFips.ByFips.TryGetValue(s.StateCode.PadLeft(2, '0'), out var info) ? info.Abbreviation : s.StateCode,
                s.Count)).ToList()));
    }

    private sealed record ReasonRow(string Reason, int Count);
}
