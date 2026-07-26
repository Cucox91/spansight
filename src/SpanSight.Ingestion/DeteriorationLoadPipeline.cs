using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Npgsql;

using NpgsqlTypes;

using SpanSight.Core.Analytics;
using SpanSight.Core.Data;
using SpanSight.Core.Domain.Lookups;
using SpanSight.Core.Ingestion;

namespace SpanSight.Ingestion;

public sealed record DeteriorationLoadSummary(
    long? RunId, string JobRunId, int MatrixRows, int Cells, int Superseded, bool Skipped);

/// <summary>
/// <c>load-deterioration</c> (FR-1.3 AC-1): publish the offline transition matrices into the serving
/// database.
/// <para>
/// Reads what <c>tools/deterioration/build-deterioration.sh</c> wrote — <c>manifest.json</c> plus the
/// two CSVs — and upserts them into <c>analytics</c> by natural key, stamping every row with the
/// <see cref="DeteriorationRun"/> that wrote it. Rows left pointing at an older run are then deleted,
/// which is what makes a re-run converge rather than accumulate (NFR-3).
/// </para>
/// <para>
/// Deliberately separate from <see cref="TrendLoadPipeline"/> and from <c>analytics.trend_run</c>:
/// FR-1.2's condition history and FR-1.3's matrices are published and rolled back independently, so a
/// bad matrix publish cannot take the drawer sparkline down with it.
/// </para>
/// </summary>
public sealed class DeteriorationLoadPipeline(SpanSightDbContext db, ILogger<DeteriorationLoadPipeline> logger)
{
    private const int BatchSize = 5000;

    public async Task<DeteriorationLoadSummary> RunAsync(
        string directory,
        bool dryRun,
        bool force,
        CancellationToken cancellationToken = default)
    {
        var manifestPath = Path.Combine(directory, "manifest.json");
        var rowPath = Path.Combine(directory, "matrix_row.csv");
        var cellPath = Path.Combine(directory, "matrix_cell.csv");

        foreach (var required in (ReadOnlySpan<string>)[manifestPath, rowPath, cellPath])
        {
            if (!File.Exists(required))
            {
                throw new FileNotFoundException(
                    $"{required} not found — build the matrices first: tools/deterioration/build-deterioration.sh",
                    required);
            }
        }

        var manifest = JsonSerializer.Deserialize<DeteriorationManifest>(
            await File.ReadAllTextAsync(manifestPath, cancellationToken),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException($"{manifestPath} is not a deterioration manifest.");

        if (manifest.SampleFloor <= 0)
        {
            // The floor is what makes a rate publishable at all (FR-1.3 AC-3). A manifest without one
            // would let the API serve every rate as sufficient, so it is refused rather than defaulted.
            throw new InvalidOperationException(
                $"{manifestPath} carries sampleFloor {manifest.SampleFloor}; the published methodology floor must be positive.");
        }

        DeteriorationRun? run = null;
        if (!dryRun)
        {
            await db.Database.MigrateAsync(cancellationToken);

            var existing = await db.DeteriorationRuns.FirstOrDefaultAsync(
                r => r.JobRunId == manifest.RunId && r.Status == DeteriorationRunStatus.Completed, cancellationToken);

            // "Already completed" is not the same as "still being served" — publishing a different job
            // sweeps this run's rows away while leaving its Completed status behind, so the rows decide
            // (the trap TrendLoadPipeline documents and this mirrors).
            var stillServed = existing is not null
                && await db.DeteriorationMatrixRows.AnyAsync(r => r.DeteriorationRunId == existing.Id, cancellationToken);

            if (stillServed && !force)
            {
                logger.LogInformation(
                    "Deterioration run {JobRunId} already published (run #{Id}); no-op. Use --force to reload.",
                    manifest.RunId, existing!.Id);
                return new DeteriorationLoadSummary(existing.Id, manifest.RunId, 0, 0, 0, Skipped: true);
            }

            if (existing is not null && !stillServed)
            {
                logger.LogInformation(
                    "Deterioration run {JobRunId} was published before but its rows have since been superseded; re-publishing.",
                    manifest.RunId);
            }

            run = existing ?? new DeteriorationRun
            {
                JobRunId = manifest.RunId,
                CatalogSha256 = manifest.CatalogSha256,
                MethodologyVersion = manifest.MethodologyVersion,
            };

            run.CatalogSha256 = manifest.CatalogSha256;
            run.MethodologyVersion = manifest.MethodologyVersion;
            run.SampleFloor = manifest.SampleFloor;
            run.StartedUtc = DateTimeOffset.UtcNow;
            run.CompletedUtc = null;
            run.FirstYear = (short)manifest.FirstYear;
            run.LastYear = (short)manifest.LastYear;
            run.YearPairs = manifest.YearPairs;
            run.StructurePairs = manifest.StructurePairs;
            run.ComponentPairs = manifest.ComponentPairs;
            run.Cohorts = manifest.Cohorts;
            run.MatrixRows = manifest.MatrixRows;
            run.Cells = manifest.Cells;
            run.RowsBelowFloor = manifest.RowsBelowFloor;
            run.Status = DeteriorationRunStatus.Running;

            if (existing is null)
            {
                db.DeteriorationRuns.Add(run);
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        var matrixRows = 0;
        var cells = 0;
        var superseded = 0;

        try
        {
            matrixRows = await LoadRowsAsync(rowPath, run, dryRun, cancellationToken);
            cells = await LoadCellsAsync(cellPath, run, dryRun, cancellationToken);

            if (matrixRows != manifest.MatrixRows)
            {
                throw new InvalidOperationException(
                    $"matrix_row.csv holds {matrixRows} rows but manifest.json claims {manifest.MatrixRows}. " +
                    "The matrices and their manifest disagree — rebuild with tools/deterioration/build-deterioration.sh.");
            }

            if (cells != manifest.Cells)
            {
                throw new InvalidOperationException(
                    $"matrix_cell.csv holds {cells} rows but manifest.json claims {manifest.Cells}. " +
                    "The matrices and their manifest disagree — rebuild with tools/deterioration/build-deterioration.sh.");
            }

            if (!dryRun)
            {
                // Convergence: anything still carrying an older run id was not in this job's output.
                superseded =
                    await db.DeteriorationMatrixCells.Where(c => c.DeteriorationRunId != run!.Id).ExecuteDeleteAsync(cancellationToken)
                    + await db.DeteriorationMatrixRows.Where(r => r.DeteriorationRunId != run!.Id).ExecuteDeleteAsync(cancellationToken);

                run!.CompletedUtc = DateTimeOffset.UtcNow;
                run.Status = DeteriorationRunStatus.Completed;
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex) when (run is not null)
        {
            run.Status = DeteriorationRunStatus.Failed;
            run.Error = ex.Message;
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        logger.LogInformation(
            "{Mode} complete: {Rows:N0} matrix rows · {Cells:N0} cells · {Superseded:N0} superseded rows removed " +
            "({Years}, {Cohorts:N0} cohorts, floor n>={Floor}, methodology {Version}, job {JobRunId}).",
            dryRun ? "Dry run" : "Deterioration load", matrixRows, cells, superseded,
            $"{manifest.FirstYear}–{manifest.LastYear}", manifest.Cohorts, manifest.SampleFloor,
            manifest.MethodologyVersion, manifest.RunId);

        return new DeteriorationLoadSummary(run?.Id, manifest.RunId, matrixRows, cells, superseded, Skipped: false);
    }

    private async Task<int> LoadRowsAsync(
        string path, DeteriorationRun? run, bool dryRun, CancellationToken cancellationToken)
    {
        var batch = new List<DeteriorationMatrixRow>(BatchSize);
        var total = 0;

        await foreach (var fields in ReadCsvAsync(path, expected: 9, cancellationToken))
        {
            var firstFromYear = short.Parse(fields[6]);
            var lastFromYear = short.Parse(fields[7]);
            var yearPairsObserved = short.Parse(fields[8]);
            var rowTotal = int.Parse(fields[5]);

            // The job checks this too; checking again here means a hand-edited or truncated CSV cannot
            // put a row into the serving tables whose span the UI would then have to render as a lie.
            if (yearPairsObserved < 1
                || firstFromYear > lastFromYear
                || yearPairsObserved > lastFromYear - firstFromYear + 1
                || yearPairsObserved > rowTotal)
            {
                throw new InvalidOperationException(
                    $"Matrix row {Describe(fields)}: {yearPairsObserved} year-pairs cannot describe " +
                    $"{firstFromYear}–{lastFromYear} over {rowTotal} pairs.");
            }

            batch.Add(new DeteriorationMatrixRow
            {
                Component = ParseComponent(fields[0], path),
                TypeGroup = fields[1],
                MaterialGroup = fields[2],
                Region = fields[3],
                FromRating = ParseRating(fields[4], path),
                RowTotal = rowTotal,
                FirstFromYear = firstFromYear,
                LastFromYear = lastFromYear,
                YearPairsObserved = yearPairsObserved,
                DeteriorationRunId = run?.Id ?? 0,
            });
            total++;

            if (batch.Count >= BatchSize)
            {
                if (!dryRun)
                {
                    await UpsertRowsAsync(batch, cancellationToken);
                }

                batch.Clear();
            }
        }

        if (!dryRun && batch.Count > 0)
        {
            await UpsertRowsAsync(batch, cancellationToken);
        }

        return total;
    }

    private async Task<int> LoadCellsAsync(
        string path, DeteriorationRun? run, bool dryRun, CancellationToken cancellationToken)
    {
        var batch = new List<DeteriorationMatrixCell>(BatchSize);
        var total = 0;

        await foreach (var fields in ReadCsvAsync(path, expected: 7, cancellationToken))
        {
            var pairs = int.Parse(fields[6]);
            if (pairs <= 0)
            {
                // Cells are stored sparse precisely so that "never observed" and "observed zero times"
                // stay distinguishable; a zero cell in the file means the job's grouping is wrong.
                throw new InvalidOperationException(
                    $"Matrix cell {Describe(fields)}: {pairs} pairs. Only non-zero cells are published.");
            }

            batch.Add(new DeteriorationMatrixCell
            {
                Component = ParseComponent(fields[0], path),
                TypeGroup = fields[1],
                MaterialGroup = fields[2],
                Region = fields[3],
                FromRating = ParseRating(fields[4], path),
                ToRating = ParseRating(fields[5], path),
                Pairs = pairs,
                DeteriorationRunId = run?.Id ?? 0,
            });
            total++;

            if (batch.Count >= BatchSize)
            {
                if (!dryRun)
                {
                    await UpsertCellsAsync(batch, cancellationToken);
                }

                batch.Clear();
            }
        }

        if (!dryRun && batch.Count > 0)
        {
            await UpsertCellsAsync(batch, cancellationToken);
        }

        return total;
    }

    private static ConditionComponent ParseComponent(string value, string path) =>
        Enum.TryParse<ConditionComponent>(value, ignoreCase: false, out var component)
            ? component
            : throw new InvalidOperationException(
                $"Unknown component '{value}' in {Path.GetFileName(path)} — expected one of " +
                $"{string.Join(", ", Enum.GetNames<ConditionComponent>())}.");

    private static short ParseRating(string value, string path) =>
        short.TryParse(value, out var rating) && rating is >= 0 and <= 9
            ? rating
            : throw new InvalidOperationException(
                $"Rating '{value}' in {Path.GetFileName(path)} is not a published NBI condition rating (0–9).");

    private static string Describe(string[] fields) => string.Join('/', fields.Take(5));

    /// <summary>Same shape as the FR-1.2 loader: arrays → <c>unnest</c> → upsert, one round trip per batch.</summary>
    private async Task UpsertRowsAsync(IReadOnlyList<DeteriorationMatrixRow> batch, CancellationToken cancellationToken)
    {
        await using var command = await CreateCommandAsync(RowUpsertSql, cancellationToken);
        AddArray(command, "components", NpgsqlDbType.Text, batch.Select(r => (object)r.Component.ToString()));
        AddArray(command, "type_groups", NpgsqlDbType.Text, batch.Select(r => (object)r.TypeGroup));
        AddArray(command, "material_groups", NpgsqlDbType.Text, batch.Select(r => (object)r.MaterialGroup));
        AddArray(command, "regions", NpgsqlDbType.Text, batch.Select(r => (object)r.Region));
        AddArray(command, "from_ratings", NpgsqlDbType.Smallint, batch.Select(r => (object)r.FromRating));
        AddArray(command, "row_totals", NpgsqlDbType.Integer, batch.Select(r => (object)r.RowTotal));
        AddArray(command, "first_years", NpgsqlDbType.Smallint, batch.Select(r => (object)r.FirstFromYear));
        AddArray(command, "last_years", NpgsqlDbType.Smallint, batch.Select(r => (object)r.LastFromYear));
        AddArray(command, "year_pairs", NpgsqlDbType.Smallint, batch.Select(r => (object)r.YearPairsObserved));
        AddArray(command, "run_ids", NpgsqlDbType.Bigint, batch.Select(r => (object)r.DeteriorationRunId));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task UpsertCellsAsync(IReadOnlyList<DeteriorationMatrixCell> batch, CancellationToken cancellationToken)
    {
        await using var command = await CreateCommandAsync(CellUpsertSql, cancellationToken);
        AddArray(command, "components", NpgsqlDbType.Text, batch.Select(c => (object)c.Component.ToString()));
        AddArray(command, "type_groups", NpgsqlDbType.Text, batch.Select(c => (object)c.TypeGroup));
        AddArray(command, "material_groups", NpgsqlDbType.Text, batch.Select(c => (object)c.MaterialGroup));
        AddArray(command, "regions", NpgsqlDbType.Text, batch.Select(c => (object)c.Region));
        AddArray(command, "from_ratings", NpgsqlDbType.Smallint, batch.Select(c => (object)c.FromRating));
        AddArray(command, "to_ratings", NpgsqlDbType.Smallint, batch.Select(c => (object)c.ToRating));
        AddArray(command, "pairs", NpgsqlDbType.Integer, batch.Select(c => (object)c.Pairs));
        AddArray(command, "run_ids", NpgsqlDbType.Bigint, batch.Select(c => (object)c.DeteriorationRunId));
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

    /// <summary>Streams a DuckDB-written CSV, reusing the ingestion splitter so quoting behaves identically.</summary>
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

    private const string RowUpsertSql = """
        INSERT INTO analytics.deterioration_matrix_row (
            component, type_group, material_group, region, from_rating, row_total,
            first_from_year, last_from_year, year_pairs_observed, deterioration_run_id)
        SELECT u.component, u.type_group, u.material_group, u.region, u.from_rating, u.row_total,
               u.first_year, u.last_year, u.year_pairs, u.run_id
        FROM unnest(@components, @type_groups, @material_groups, @regions, @from_ratings, @row_totals,
                    @first_years, @last_years, @year_pairs, @run_ids)
        AS u(component, type_group, material_group, region, from_rating, row_total,
             first_year, last_year, year_pairs, run_id)
        ON CONFLICT (component, type_group, material_group, region, from_rating) DO UPDATE SET
            row_total = EXCLUDED.row_total,
            first_from_year = EXCLUDED.first_from_year,
            last_from_year = EXCLUDED.last_from_year,
            year_pairs_observed = EXCLUDED.year_pairs_observed,
            deterioration_run_id = EXCLUDED.deterioration_run_id;
        """;

    private const string CellUpsertSql = """
        INSERT INTO analytics.deterioration_matrix_cell (
            component, type_group, material_group, region, from_rating, to_rating, pairs, deterioration_run_id)
        SELECT u.component, u.type_group, u.material_group, u.region, u.from_rating, u.to_rating, u.pairs, u.run_id
        FROM unnest(@components, @type_groups, @material_groups, @regions, @from_ratings, @to_ratings, @pairs, @run_ids)
        AS u(component, type_group, material_group, region, from_rating, to_rating, pairs, run_id)
        ON CONFLICT (component, type_group, material_group, region, from_rating, to_rating) DO UPDATE SET
            pairs = EXCLUDED.pairs,
            deterioration_run_id = EXCLUDED.deterioration_run_id;
        """;

    /// <summary>The subset of <c>manifest.json</c> the loader needs; the job writes more for humans.</summary>
    private sealed record DeteriorationManifest(
        string RunId,
        string CatalogSha256,
        string MethodologyVersion,
        int SampleFloor,
        int FirstYear,
        int LastYear,
        int YearPairs,
        long StructurePairs,
        long ComponentPairs,
        int Cohorts,
        int MatrixRows,
        int Cells,
        int RowsBelowFloor);
}
