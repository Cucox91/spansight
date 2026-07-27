using Microsoft.EntityFrameworkCore;

using SpanSight.Core.Analytics;
using SpanSight.Core.Data;
using SpanSight.Core.Domain.Lookups;

namespace SpanSight.Api.Endpoints;

public static class QaEndpoints
{
    /// <summary>
    /// Stated next to the coverage figure and rendered beside it in the UI, so what was joined — and
    /// what a disagreement does and does not mean — travels with the number (FR-1.5 AC-2, GR-6).
    /// </summary>
    public const string CountyJoinMethodNote =
        "Each published bridge coordinate was tested against the TIGER county polygons with " +
        "ST_Within, which excludes the boundary: a structure sitting exactly on a county line is " +
        "inside neither county and is quarantined rather than assigned to one of them. The result " +
        "is then compared with the county code FHWA published in item 3. A disagreement is a " +
        "measurement, not a correction — item 3 remains the county this product reports " +
        "everywhere, including in the filters, the trend series and the county report card.";

    public static RouteGroupBuilder MapQa(this RouteGroupBuilder group)
    {
        group.MapGet("/qa/summary", SummaryAsync)
            .WithSummary("Data-quality report")
            .WithDescription(
                "Latest ingestion run with reject breakdown by reason code and by state (FR-0.2, " +
                "UC-0.6), plus the bridge→county join coverage published by " +
                "tools/census/join-counties.sh (FR-1.5 AC-2): the share of served bridges matched to " +
                "a county polygon, every miss itemised by reason, and the largest disagreements " +
                "between the containing polygon and the published county code. Figures reconcile " +
                "with the run summaries by construction — same tables.");

        return group;
    }

    private static async Task<IResult> SummaryAsync(SpanSightDbContext db, CancellationToken cancellationToken)
    {
        var latest = await db.IngestionRuns.AsNoTracking()
            .Where(r => r.Status == IngestionRunStatus.Completed)
            .OrderByDescending(r => r.StartedUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var countyJoin = await CountyJoinAsync(db, cancellationToken);

        if (latest is null)
        {
            return TypedResults.Ok(new QaSummaryDto(null, [], [], countyJoin));
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
                s.Count)).ToList(),
            countyJoin));
    }

    /// <summary>
    /// FR-1.5 AC-2 — the join coverage block. Null until the join has been published, so the page can
    /// say the measurement has not been taken rather than render 0% and imply it failed.
    /// </summary>
    private static async Task<CountyJoinCoverageDto?> CountyJoinAsync(
        SpanSightDbContext db, CancellationToken cancellationToken)
    {
        // Read from the run stamped on the rows being served, not from the newest completed run. A
        // load converges by deleting rows left pointing at an older run, so a run can be Completed
        // and serving nothing — republishing an earlier job is exactly that case. Taking the newest
        // id would attach one job's coverage percentage to another job's counties (the trap FR-1.2
        // and FR-1.3 both document).
        var runId = await db.CensusCounties.AsNoTracking()
            .OrderByDescending(c => c.CountyJoinRunId)
            .Select(c => (long?)c.CountyJoinRunId)
            .FirstOrDefaultAsync(cancellationToken);

        if (runId is null)
        {
            return null;
        }

        var run = await db.CountyJoinRuns.AsNoTracking()
            .FirstOrDefaultAsync(
                r => r.Id == runId && r.Status == CountyJoinRunStatus.Completed, cancellationToken);

        if (run is null)
        {
            return null;
        }

        var reasons = await db.CountyJoinMisses.AsNoTracking()
            .Where(m => m.CountyJoinRunId == run.Id)
            .GroupBy(m => m.Reason)
            .Select(g => new
            {
                Reason = g.Key,
                Structures = g.Count(),
                // Both null when every miss in the group fell outside the search radius, which is the
                // honest answer: the job stopped looking rather than measured a distance.
                Max = g.Max(m => m.NearestDistanceMeters),
                Distances = g.Where(m => m.NearestDistanceMeters != null)
                             .Select(m => m.NearestDistanceMeters!.Value).ToList(),
            })
            .ToListAsync(cancellationToken);

        // The largest disagreements, joined to their names on both sides. The published side may
        // legitimately name a county TIGER retired, so that join is a LEFT one and a missing name is
        // reported as such rather than swallowing the row.
        var disagreements = await db.CountyJoinDisagreements.AsNoTracking()
            .Where(d => d.CountyJoinRunId == run.Id)
            .OrderByDescending(d => d.Bridges)
            .ThenBy(d => d.CountyFips)
            .Take(15)
            .Join(db.CensusCounties.AsNoTracking(), d => d.CountyFips, c => c.CountyFips,
                (d, c) => new { Disagreement = d, Containing = c.NameLsad })
            .GroupJoin(db.CensusCounties.AsNoTracking(), x => x.Disagreement.NbiCountyFips, c => c.CountyFips,
                (x, published) => new { x.Disagreement, x.Containing, Published = published })
            .ToListAsync(cancellationToken);

        var retiredCodeBridges = await db.CountyJoinDisagreements.AsNoTracking()
            .Where(d => d.CountyJoinRunId == run.Id && d.NbiFipsInTiger == false)
            .SumAsync(d => (long)d.Bridges, cancellationToken);

        return new CountyJoinCoverageDto(
            run.Bridges,
            run.Matched,
            run.Unmatched,
            Percent(run.Matched, run.Bridges) ?? 0,
            run.Structures,
            run.StructuresMatched,
            Percent(run.StructuresMatched, run.Structures) ?? 0,
            run.Agree,
            Percent(run.Agree, run.Matched),
            run.DifferentCountySameState,
            run.DifferentState,
            run.CountyNotPublished,
            run.Counties,
            run.CountiesWithoutPopulation,
            [.. reasons
                .OrderByDescending(r => r.Structures)
                .Select(r => new CountyJoinMissReasonDto(r.Reason, r.Structures, Median(r.Distances), r.Max))],
            [.. disagreements.Select(x => new CountyJoinDisagreementDto(
                x.Disagreement.NbiCountyFips,
                x.Published.Select(p => p.NameLsad).FirstOrDefault(),
                x.Disagreement.CountyFips,
                x.Containing,
                x.Disagreement.Kind,
                x.Disagreement.Bridges,
                x.Disagreement.NbiFipsInTiger))],
            retiredCodeBridges,
            CountyJoinMethodNote,
            new CountyJoinProvenanceDto(
                run.JobRunId, run.CatalogSha256, run.MethodVersion,
                run.ContainmentPredicate, run.CompletedUtc));
    }

    /// <summary>Percentage to four decimals; a coverage of 99.9926% must not round to 100%.</summary>
    private static double? Percent(long part, long total) =>
        total == 0 ? null : Math.Round(100d * part / total, 4);

    private static long? Median(List<long> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var sorted = values.Order().ToList();
        // Even counts take the lower of the two middles rather than averaging: these are measured
        // distances to real polygons, and the mean of two of them is a distance to nothing.
        return sorted[(sorted.Count - 1) / 2];
    }

    private sealed record ReasonRow(string Reason, int Count);
}
