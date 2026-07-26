using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Npgsql;

using NpgsqlTypes;

using SpanSight.Core.Analytics;
using SpanSight.Core.Data;
using SpanSight.Core.Ingestion;

namespace SpanSight.Ingestion;

public sealed record TrendLoadSummary(long? RunId, string JobRunId, int SeriesRows, int RollupRows, int Superseded, bool Skipped);

/// <summary>
/// <c>load-trends</c> (FR-1.2 AC-1): publish the offline condition-history aggregates into the
/// serving database.
/// <para>
/// Reads what <c>tools/trends/build-trends.sh</c> wrote — <c>manifest.json</c> plus the two CSVs —
/// and upserts them into <c>analytics</c> by natural key, stamping every row with the
/// <see cref="TrendRun"/> that wrote it. Rows left pointing at an older run are then deleted, which
/// is what makes a re-run converge rather than accumulate: a structure that disappears from the job
/// output disappears from the serving tables in the same load (NFR-3).
/// </para>
/// <para>
/// Re-loading the same job run id is a logged no-op, as with snapshot loads (FR-0.1 AC-2).
/// Rebuilding the aggregates produces a new run id and re-publishes.
/// </para>
/// </summary>
public sealed class TrendLoadPipeline(SpanSightDbContext db, ILogger<TrendLoadPipeline> logger)
{
    private const int BatchSize = 5000;

    public async Task<TrendLoadSummary> RunAsync(
        string directory,
        bool dryRun,
        bool force,
        CancellationToken cancellationToken = default)
    {
        var manifestPath = Path.Combine(directory, "manifest.json");
        var seriesPath = Path.Combine(directory, "bridge_series.csv");
        var rollupPath = Path.Combine(directory, "rollup.csv");

        foreach (var required in (ReadOnlySpan<string>)[manifestPath, seriesPath, rollupPath])
        {
            if (!File.Exists(required))
            {
                throw new FileNotFoundException(
                    $"{required} not found — build the aggregates first: tools/trends/build-trends.sh", required);
            }
        }

        var manifest = JsonSerializer.Deserialize<TrendManifest>(
            await File.ReadAllTextAsync(manifestPath, cancellationToken),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException($"{manifestPath} is not a trend manifest.");

        TrendRun? run = null;
        if (!dryRun)
        {
            await db.Database.MigrateAsync(cancellationToken);

            var existing = await db.TrendRuns.FirstOrDefaultAsync(
                r => r.JobRunId == manifest.RunId && r.Status == TrendRunStatus.Completed, cancellationToken);

            // "Already completed" is not the same as "still being served": publishing a different
            // job sweeps this run's rows away (see the convergence delete below) while leaving its
            // Completed status behind. Skipping on status alone would make re-publishing an earlier
            // job a silent no-op that never restores its data, so the rows are what decide.
            var stillServed = existing is not null
                && await db.BridgeConditionSeries.AnyAsync(s => s.TrendRunId == existing.Id, cancellationToken);

            if (stillServed && !force)
            {
                logger.LogInformation(
                    "Trend run {JobRunId} already published (run #{Id}); no-op. Use --force to reload.",
                    manifest.RunId, existing!.Id);
                return new TrendLoadSummary(existing.Id, manifest.RunId, 0, 0, 0, Skipped: true);
            }

            if (existing is not null && !stillServed)
            {
                logger.LogInformation(
                    "Trend run {JobRunId} was published before but its rows have since been superseded; re-publishing.",
                    manifest.RunId);
            }

            run = new TrendRun
            {
                JobRunId = manifest.RunId,
                CatalogSha256 = manifest.CatalogSha256,
                StartedUtc = DateTimeOffset.UtcNow,
                FirstYear = (short)manifest.FirstYear,
                LastYear = (short)manifest.LastYear,
                Vintages = manifest.Vintages,
                Bridges = manifest.Bridges,
                Observations = manifest.Observations,
                RollupRows = manifest.RollupRows,
                Status = TrendRunStatus.Running,
            };

            // A re-run of the same job id lands on the unique index; reuse the row instead.
            if (existing is not null)
            {
                existing.StartedUtc = run.StartedUtc;
                existing.CompletedUtc = null;
                existing.Status = TrendRunStatus.Running;
                run = existing;
            }
            else
            {
                db.TrendRuns.Add(run);
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        var seriesRows = 0;
        var rollupRows = 0;
        var superseded = 0;

        try
        {
            seriesRows = await LoadSeriesAsync(seriesPath, run, dryRun, cancellationToken);
            rollupRows = await LoadRollupsAsync(rollupPath, run, dryRun, cancellationToken);

            if (seriesRows != manifest.Bridges)
            {
                throw new InvalidOperationException(
                    $"bridge_series.csv holds {seriesRows} rows but manifest.json claims {manifest.Bridges}. " +
                    "The aggregates and their manifest disagree — rebuild with tools/trends/build-trends.sh.");
            }

            if (rollupRows != manifest.RollupRows)
            {
                throw new InvalidOperationException(
                    $"rollup.csv holds {rollupRows} rows but manifest.json claims {manifest.RollupRows}. " +
                    "The aggregates and their manifest disagree — rebuild with tools/trends/build-trends.sh.");
            }

            if (!dryRun)
            {
                // Convergence: anything still carrying an older run id was not in this job's output.
                superseded =
                    await db.BridgeConditionSeries.Where(s => s.TrendRunId != run!.Id).ExecuteDeleteAsync(cancellationToken)
                    + await db.ConditionRollups.Where(r => r.TrendRunId != run!.Id).ExecuteDeleteAsync(cancellationToken);

                run!.CompletedUtc = DateTimeOffset.UtcNow;
                run.Status = TrendRunStatus.Completed;
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex) when (run is not null)
        {
            run.Status = TrendRunStatus.Failed;
            run.Error = ex.Message;
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        logger.LogInformation(
            "{Mode} complete: {Series:N0} bridge series · {Rollups:N0} rollup rows · {Superseded:N0} superseded rows removed ({Years}, job {JobRunId}).",
            dryRun ? "Dry run" : "Trend load", seriesRows, rollupRows, superseded,
            $"{manifest.FirstYear}–{manifest.LastYear}", manifest.RunId);

        return new TrendLoadSummary(run?.Id, manifest.RunId, seriesRows, rollupRows, superseded, Skipped: false);
    }

    private async Task<int> LoadSeriesAsync(string path, TrendRun? run, bool dryRun, CancellationToken cancellationToken)
    {
        var batch = new List<BridgeConditionSeries>(BatchSize);
        var total = 0;

        await foreach (var fields in ReadCsvAsync(path, expected: 6, cancellationToken))
        {
            var firstYear = short.Parse(fields[2]);
            var lastYear = short.Parse(fields[3]);
            var ratings = fields[5];

            // The job checks this too; checking again here means a hand-edited or truncated CSV
            // cannot put a series into the serving tables that the API would then have to guess at.
            if (!ConditionSeriesCodec.IsWellFormed(firstYear, lastYear, ratings))
            {
                throw new InvalidOperationException(
                    $"Malformed series for {fields[0]}-{fields[1]}: '{ratings}' does not describe {firstYear}–{lastYear}.");
            }

            batch.Add(new BridgeConditionSeries
            {
                StateCode = fields[0],
                StructureNumber = fields[1],
                FirstYear = firstYear,
                LastYear = lastYear,
                ObservedYears = short.Parse(fields[4]),
                Ratings = ratings,
                TrendRunId = run?.Id ?? 0,
            });
            total++;

            if (!dryRun && batch.Count >= BatchSize)
            {
                await UpsertSeriesAsync(batch, cancellationToken);
                batch.Clear();
            }
            else if (dryRun && batch.Count >= BatchSize)
            {
                batch.Clear();
            }
        }

        if (!dryRun && batch.Count > 0)
        {
            await UpsertSeriesAsync(batch, cancellationToken);
        }

        return total;
    }

    private async Task<int> LoadRollupsAsync(string path, TrendRun? run, bool dryRun, CancellationToken cancellationToken)
    {
        var batch = new List<ConditionRollup>(BatchSize);
        var total = 0;

        await foreach (var fields in ReadCsvAsync(path, expected: 8, cancellationToken))
        {
            var level = fields[0] switch
            {
                "state" => RollupLevel.State,
                "county" => RollupLevel.County,
                var other => throw new InvalidOperationException($"Unknown rollup level '{other}' in {path}."),
            };

            var total_ = int.Parse(fields[3]);
            var good = int.Parse(fields[4]);
            var fair = int.Parse(fields[5]);
            var poor = int.Parse(fields[6]);
            var unknown = int.Parse(fields[7]);
            if (good + fair + poor + unknown != total_)
            {
                throw new InvalidOperationException(
                    $"Rollup {fields[0]} {fields[1]} {fields[2]}: {good}+{fair}+{poor}+{unknown} != {total_}.");
            }

            batch.Add(new ConditionRollup
            {
                Level = level,
                Fips = fields[1],
                VintageYear = short.Parse(fields[2]),
                Total = total_,
                Good = good,
                Fair = fair,
                Poor = poor,
                Unknown = unknown,
                TrendRunId = run?.Id ?? 0,
            });
            total++;

            if (!dryRun && batch.Count >= BatchSize)
            {
                await UpsertRollupsAsync(batch, cancellationToken);
                batch.Clear();
            }
            else if (dryRun && batch.Count >= BatchSize)
            {
                batch.Clear();
            }
        }

        if (!dryRun && batch.Count > 0)
        {
            await UpsertRollupsAsync(batch, cancellationToken);
        }

        return total;
    }

    /// <summary>Same shape as the snapshot loader: arrays → <c>unnest</c> → upsert, one round trip per batch.</summary>
    private async Task UpsertSeriesAsync(IReadOnlyList<BridgeConditionSeries> batch, CancellationToken cancellationToken)
    {
        await using var command = await CreateCommandAsync(SeriesUpsertSql, cancellationToken);
        AddArray(command, "state_codes", NpgsqlDbType.Text, batch.Select(s => (object)s.StateCode));
        AddArray(command, "structure_numbers", NpgsqlDbType.Text, batch.Select(s => (object)s.StructureNumber));
        AddArray(command, "first_years", NpgsqlDbType.Smallint, batch.Select(s => (object)s.FirstYear));
        AddArray(command, "last_years", NpgsqlDbType.Smallint, batch.Select(s => (object)s.LastYear));
        AddArray(command, "observed_years", NpgsqlDbType.Smallint, batch.Select(s => (object)s.ObservedYears));
        AddArray(command, "ratings", NpgsqlDbType.Text, batch.Select(s => (object)s.Ratings));
        AddArray(command, "run_ids", NpgsqlDbType.Bigint, batch.Select(s => (object)s.TrendRunId));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task UpsertRollupsAsync(IReadOnlyList<ConditionRollup> batch, CancellationToken cancellationToken)
    {
        await using var command = await CreateCommandAsync(RollupUpsertSql, cancellationToken);
        AddArray(command, "levels", NpgsqlDbType.Text, batch.Select(r => (object)r.Level.ToString()));
        AddArray(command, "fips", NpgsqlDbType.Text, batch.Select(r => (object)r.Fips));
        AddArray(command, "years", NpgsqlDbType.Smallint, batch.Select(r => (object)r.VintageYear));
        AddArray(command, "totals", NpgsqlDbType.Integer, batch.Select(r => (object)r.Total));
        AddArray(command, "goods", NpgsqlDbType.Integer, batch.Select(r => (object)r.Good));
        AddArray(command, "fairs", NpgsqlDbType.Integer, batch.Select(r => (object)r.Fair));
        AddArray(command, "poors", NpgsqlDbType.Integer, batch.Select(r => (object)r.Poor));
        AddArray(command, "unknowns", NpgsqlDbType.Integer, batch.Select(r => (object)r.Unknown));
        AddArray(command, "run_ids", NpgsqlDbType.Bigint, batch.Select(r => (object)r.TrendRunId));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<NpgsqlCommand> CreateCommandAsync(string sql, CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var command = connection.CreateCommand();
        command.CommandText = sql;
        return command;
    }

    private static void AddArray(NpgsqlCommand command, string name, NpgsqlDbType elementType, IEnumerable<object> values) =>
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Array | elementType) { Value = values.ToArray() });

    /// <summary>
    /// Streams a DuckDB-written CSV: header row, comma separated, quoted where needed. Reuses the
    /// ingestion splitter so quoting behaves identically to the snapshot path.
    /// </summary>
    private static async IAsyncEnumerable<string[]> ReadCsvAsync(
        string path,
        int expected,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(path);
        var header = await reader.ReadLineAsync(cancellationToken);
        if (header is null)
        {
            yield break;
        }

        var lineNumber = 1;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            lineNumber++;
            if (line.Length == 0)
            {
                continue;
            }

            var fields = DelimitedLineSplitter.Split(line);
            if (fields.Length != expected)
            {
                throw new InvalidOperationException(
                    $"{Path.GetFileName(path)} line {lineNumber}: expected {expected} fields, found {fields.Length}.");
            }

            yield return fields;
        }
    }

    private const string SeriesUpsertSql = """
        INSERT INTO analytics.bridge_condition_series (
            state_code, structure_number, first_year, last_year, observed_years, ratings, trend_run_id)
        SELECT u.state_code, u.structure_number, u.first_year, u.last_year, u.observed_years, u.ratings, u.run_id
        FROM unnest(
            @state_codes, @structure_numbers, @first_years, @last_years, @observed_years, @ratings, @run_ids)
        AS u(state_code, structure_number, first_year, last_year, observed_years, ratings, run_id)
        ON CONFLICT (state_code, structure_number) DO UPDATE SET
            first_year = EXCLUDED.first_year,
            last_year = EXCLUDED.last_year,
            observed_years = EXCLUDED.observed_years,
            ratings = EXCLUDED.ratings,
            trend_run_id = EXCLUDED.trend_run_id;
        """;

    private const string RollupUpsertSql = """
        INSERT INTO analytics.condition_rollup (
            level, fips, vintage_year, total, good, fair, poor, unknown, trend_run_id)
        SELECT u.level, u.fips, u.year, u.total, u.good, u.fair, u.poor, u.unknown, u.run_id
        FROM unnest(@levels, @fips, @years, @totals, @goods, @fairs, @poors, @unknowns, @run_ids)
        AS u(level, fips, year, total, good, fair, poor, unknown, run_id)
        ON CONFLICT (level, fips, vintage_year) DO UPDATE SET
            total = EXCLUDED.total,
            good = EXCLUDED.good,
            fair = EXCLUDED.fair,
            poor = EXCLUDED.poor,
            unknown = EXCLUDED.unknown,
            trend_run_id = EXCLUDED.trend_run_id;
        """;

    /// <summary>The subset of <c>manifest.json</c> the loader needs; the job writes more for humans.</summary>
    private sealed record TrendManifest(
        string RunId,
        string CatalogSha256,
        int FirstYear,
        int LastYear,
        int Vintages,
        int Bridges,
        long Observations,
        int RollupRows);
}
