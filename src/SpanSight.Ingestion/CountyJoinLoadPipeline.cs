using System.Globalization;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Npgsql;

using NpgsqlTypes;

using SpanSight.Core.Analytics;
using SpanSight.Core.Data;
using SpanSight.Core.Ingestion;

namespace SpanSight.Ingestion;

public sealed record CountyJoinLoadSummary(
    long? RunId, string JobRunId, int Counties, int Misses, int Disagreements, int Superseded, bool Skipped);

/// <summary>
/// <c>load-county-join</c> (FR-1.5 AC-2/AC-3): publish the offline bridge→county join into the
/// serving database.
/// <para>
/// Reads what <c>tools/census/join-counties.sh</c> wrote — <c>manifest.json</c> plus the three CSVs —
/// and upserts them into <c>analytics</c> by natural key, stamping every row with the
/// <see cref="CountyJoinRun"/> that wrote it. Rows left pointing at an older run are then deleted,
/// which is what makes a re-run converge rather than accumulate (NFR-3).
/// </para>
/// <para>
/// Deliberately separate from <see cref="TrendLoadPipeline"/> and
/// <see cref="DeteriorationLoadPipeline"/>, and it writes nothing outside <c>analytics</c>: the
/// county a bridge is reported in stays the county item 3 published, so <c>core.bridge</c> is never
/// touched by a job whose entire output is a measurement of how often the two disagree (GR-6).
/// </para>
/// </summary>
public sealed class CountyJoinLoadPipeline(SpanSightDbContext db, ILogger<CountyJoinLoadPipeline> logger)
{
    private const int BatchSize = 5000;

    public async Task<CountyJoinLoadSummary> RunAsync(
        string directory,
        bool dryRun,
        bool force,
        CancellationToken cancellationToken = default)
    {
        var manifestPath = Path.Combine(directory, "manifest.json");
        var countyPath = Path.Combine(directory, "county.csv");
        var missPath = Path.Combine(directory, "miss.csv");
        var disagreementPath = Path.Combine(directory, "disagreement.csv");

        foreach (var required in (ReadOnlySpan<string>)[manifestPath, countyPath, missPath, disagreementPath])
        {
            if (!File.Exists(required))
            {
                throw new FileNotFoundException(
                    $"{required} not found — build the join first: tools/census/join-counties.sh",
                    required);
            }
        }

        var manifest = JsonSerializer.Deserialize<CountyJoinManifest>(
            await File.ReadAllTextAsync(manifestPath, cancellationToken),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException($"{manifestPath} is not a county-join manifest.");

        if (manifest.Bridges <= 0)
        {
            // The coverage share is the published deliverable of FR-1.5 AC-2, and a zero denominator
            // makes it undefined. Refused rather than defaulted, because the alternative is a QA page
            // stating a percentage nobody computed.
            throw new InvalidOperationException(
                $"{manifestPath} reports {manifest.Bridges} bridges; a join over no bridges has no coverage to publish.");
        }

        if (manifest.Matched + manifest.Unmatched != manifest.Bridges)
        {
            throw new InvalidOperationException(
                $"{manifestPath} reports {manifest.Matched} matched + {manifest.Unmatched} unmatched, " +
                $"which is not the {manifest.Bridges} bridges it says were joined. Rebuild with " +
                "tools/census/join-counties.sh.");
        }

        CountyJoinRun? run = null;
        if (!dryRun)
        {
            await db.Database.MigrateAsync(cancellationToken);

            // Looked up by job id alone, whatever its status — job_run_id is uniquely indexed, so
            // filtering on Completed here would make a retry after a failed load throw a raw
            // duplicate-key error from SaveChanges, and a failed load is exactly when an operator
            // re-runs the command (the trap DeteriorationLoadPipeline documents and this mirrors).
            var existing = await db.CountyJoinRuns.FirstOrDefaultAsync(
                r => r.JobRunId == manifest.RunId, cancellationToken);

            // "Already completed" is not "still being served": publishing a different job sweeps this
            // run's rows away while leaving its Completed status behind, so the rows decide.
            var stillServed = existing is { Status: CountyJoinRunStatus.Completed }
                && await db.CensusCounties.AnyAsync(c => c.CountyJoinRunId == existing.Id, cancellationToken);

            if (stillServed && !force)
            {
                logger.LogInformation(
                    "County join {JobRunId} already published (run #{Id}); no-op. Use --force to reload.",
                    manifest.RunId, existing!.Id);
                return new CountyJoinLoadSummary(existing.Id, manifest.RunId, 0, 0, 0, 0, Skipped: true);
            }

            if (existing is not null && !stillServed)
            {
                logger.LogInformation(
                    "County join {JobRunId} was published before but its rows have since been superseded; re-publishing.",
                    manifest.RunId);
            }

            run = existing ?? new CountyJoinRun
            {
                JobRunId = manifest.RunId,
                CatalogSha256 = manifest.CatalogSha256,
                MethodVersion = manifest.MethodVersion,
                ContainmentPredicate = manifest.ContainmentPredicate,
            };

            run.CatalogSha256 = manifest.CatalogSha256;
            run.MethodVersion = manifest.MethodVersion;
            run.ContainmentPredicate = manifest.ContainmentPredicate;
            run.StartedUtc = DateTimeOffset.UtcNow;
            run.CompletedUtc = null;
            run.Bridges = manifest.Bridges;
            run.Matched = manifest.Matched;
            run.Unmatched = manifest.Unmatched;
            run.Structures = manifest.Structures;
            run.StructuresMatched = manifest.StructuresMatched;
            run.Agree = manifest.Agree;
            run.DifferentCountySameState = manifest.DifferentCountySameState;
            run.DifferentState = manifest.DifferentState;
            run.CountyNotPublished = manifest.CountyNotPublished;
            run.Counties = manifest.Counties;
            run.CountiesWithoutPopulation = manifest.CountiesWithoutPopulation;
            run.Misses = manifest.Misses;
            run.Disagreements = manifest.Disagreements;
            run.DisagreementBridges = manifest.DisagreementBridges;
            run.Status = CountyJoinRunStatus.Running;

            if (existing is null)
            {
                db.CountyJoinRuns.Add(run);
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        var counties = 0;
        var misses = 0;
        var disagreements = 0;
        var superseded = 0;

        // One transaction around the whole publish. The manifest reconciliation below can only run
        // once all three files have been read, so without this a rejected build's rows would already
        // be on the serving tables when it throws — with the convergence delete never reached, the QA
        // page would state a coverage share from the manifest against county rows from another run.
        // The rollback puts the tables back exactly as they were; only the run row's Failed status
        // survives, written on its own connection afterwards so the operator can see what happened.
        await using var transaction = dryRun ? null : await db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            counties = await LoadCountiesAsync(countyPath, run, dryRun, cancellationToken);
            misses = await LoadMissesAsync(missPath, run, dryRun, cancellationToken);
            disagreements = await LoadDisagreementsAsync(disagreementPath, run, dryRun, cancellationToken);

            Reconcile(counties, manifest.Counties, "county.csv", "counties");
            Reconcile(misses, manifest.Misses, "miss.csv", "misses");
            Reconcile(disagreements, manifest.Disagreements, "disagreement.csv", "disagreements");

            // The quarantine has to account for every unmatched bridge. A miss file that is short is
            // the one failure that would make coverage look honest while hiding the structures it
            // could not place, which is the opposite of what AC-2 asks for.
            if (misses != manifest.Unmatched)
            {
                throw new InvalidOperationException(
                    $"miss.csv holds {misses} rows but manifest.json reports {manifest.Unmatched} unmatched " +
                    "bridges. Every unmatched bridge must be quarantined with a reason (FR-1.5 AC-2).");
            }

            if (!dryRun)
            {
                // Convergence: anything still carrying an older run id was not in this job's output.
                superseded =
                    await db.CountyJoinDisagreements.Where(d => d.CountyJoinRunId != run!.Id).ExecuteDeleteAsync(cancellationToken)
                    + await db.CountyJoinMisses.Where(m => m.CountyJoinRunId != run!.Id).ExecuteDeleteAsync(cancellationToken)
                    + await db.CensusCounties.Where(c => c.CountyJoinRunId != run!.Id).ExecuteDeleteAsync(cancellationToken);

                run!.CompletedUtc = DateTimeOffset.UtcNow;
                run.Status = CountyJoinRunStatus.Completed;
                await db.SaveChangesAsync(cancellationToken);
                await transaction!.CommitAsync(cancellationToken);
            }
        }
        catch (Exception ex) when (run is not null)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }

            // The run row was written before the transaction opened, so it is still there to mark —
            // but EF is holding the rolled-back Completed/Running edits, so they are discarded first.
            db.ChangeTracker.Clear();
            var failed = await db.CountyJoinRuns.FirstAsync(r => r.Id == run.Id, CancellationToken.None);
            failed.Status = CountyJoinRunStatus.Failed;
            failed.Error = ex.Message;
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        logger.LogInformation(
            "{Mode} complete: {Counties:N0} counties · {Misses:N0} quarantined misses · " +
            "{Disagreements:N0} disagreement pairs · {Superseded:N0} superseded rows removed " +
            "({Matched:N0}/{Bridges:N0} matched, {Predicate}, method {Version}, job {JobRunId}).",
            dryRun ? "Dry run" : "County join load", counties, misses, disagreements, superseded,
            manifest.Matched, manifest.Bridges, manifest.ContainmentPredicate,
            manifest.MethodVersion, manifest.RunId);

        return new CountyJoinLoadSummary(
            run?.Id, manifest.RunId, counties, misses, disagreements, superseded, Skipped: false);
    }

    private static void Reconcile(int actual, int claimed, string file, string noun)
    {
        if (actual != claimed)
        {
            throw new InvalidOperationException(
                $"{file} holds {actual} rows but manifest.json claims {claimed} {noun}. " +
                "The join and its manifest disagree — rebuild with tools/census/join-counties.sh.");
        }
    }

    private async Task<int> LoadCountiesAsync(
        string path, CountyJoinRun? run, bool dryRun, CancellationToken cancellationToken)
    {
        var batch = new List<CensusCounty>(BatchSize);
        var total = 0;

        await foreach (var fields in ReadCsvAsync(path, expected: 12, cancellationToken))
        {
            var countyFips = fields[0];
            var stateFips = fields[1];

            // Re-checked on read so a hand-edited or truncated CSV cannot put a county into the
            // serving tables whose key the report card would then fail to resolve. The job checks the
            // same shape; this is the second door on the same room.
            if (countyFips.Length != 5
                || !countyFips.All(char.IsAsciiDigit)
                || stateFips.Length != 2
                || !countyFips.StartsWith(stateFips, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"County '{countyFips}' in {Path.GetFileName(path)}: a county FIPS is 5 digits " +
                    $"beginning with its 2-digit state FIPS (got state '{stateFips}').");
            }

            var population = ParseNullableLong(fields[7]);
            if (population is < 0)
            {
                // ACS jam values are large negatives and the converter already nulls them; one
                // arriving here means the staging step was bypassed.
                throw new InvalidOperationException(
                    $"County '{countyFips}' in {Path.GetFileName(path)} carries population {population}. " +
                    "A negative ACS value is a jam code, not an estimate.");
            }

            batch.Add(new CensusCounty
            {
                CountyFips = countyFips,
                StateFips = stateFips,
                Name = fields[2],
                NameLsad = fields[3],
                LandAreaM2 = long.Parse(fields[4], CultureInfo.InvariantCulture),
                WaterAreaM2 = long.Parse(fields[5], CultureInfo.InvariantCulture),
                TigerVintage = short.Parse(fields[6], CultureInfo.InvariantCulture),
                Population = population,
                PopulationMoe = ParseNullableLong(fields[8]),
                AcsVintage = ParseNullableShort(fields[9]),
                AcsPeriod = NullIfEmpty(fields[10]),
                AcsTable = NullIfEmpty(fields[11]),
                CountyJoinRunId = run?.Id ?? 0,
            });
            total++;

            if (batch.Count >= BatchSize)
            {
                if (!dryRun)
                {
                    await UpsertCountiesAsync(batch, cancellationToken);
                }

                batch.Clear();
            }
        }

        if (!dryRun && batch.Count > 0)
        {
            await UpsertCountiesAsync(batch, cancellationToken);
        }

        return total;
    }

    private async Task<int> LoadMissesAsync(
        string path, CountyJoinRun? run, bool dryRun, CancellationToken cancellationToken)
    {
        var batch = new List<CountyJoinMiss>(BatchSize);
        var total = 0;

        await foreach (var fields in ReadCsvAsync(path, expected: 10, cancellationToken))
        {
            var reason = fields[4];
            var touching = int.Parse(fields[5], CultureInfo.InvariantCulture);

            // The two reasons must stay distinguishable facts about the geometry, not labels: a
            // boundary miss touches a polygon, an outside miss does not. The job asserts this too;
            // asserting it again here means the QA page cannot render a reason the evidence denies.
            if (reason is not ("on_county_boundary" or "outside_all_county_polygons")
                || (reason == "on_county_boundary" && touching < 1)
                || (reason == "outside_all_county_polygons" && touching != 0))
            {
                throw new InvalidOperationException(
                    $"Miss {fields[0]}/{fields[1]}/{fields[2]} in {Path.GetFileName(path)}: reason " +
                    $"'{reason}' with {touching} touching county polygon(s) is not a shape the join produces.");
            }

            batch.Add(new CountyJoinMiss
            {
                StateCode = fields[0],
                StructureNumber = fields[1],
                RecordType = fields[2],
                NbiCountyFips = NullIfEmpty(fields[3]),
                Reason = reason,
                TouchingCounties = touching,
                NearestCountyFips = NullIfEmpty(fields[6]),
                NearestDistanceMeters = ParseNullableLong(fields[7]),
                Longitude = double.Parse(fields[8], CultureInfo.InvariantCulture),
                Latitude = double.Parse(fields[9], CultureInfo.InvariantCulture),
                CountyJoinRunId = run?.Id ?? 0,
            });
            total++;

            if (batch.Count >= BatchSize)
            {
                if (!dryRun)
                {
                    await UpsertMissesAsync(batch, cancellationToken);
                }

                batch.Clear();
            }
        }

        if (!dryRun && batch.Count > 0)
        {
            await UpsertMissesAsync(batch, cancellationToken);
        }

        return total;
    }

    private async Task<int> LoadDisagreementsAsync(
        string path, CountyJoinRun? run, bool dryRun, CancellationToken cancellationToken)
    {
        var batch = new List<CountyJoinDisagreement>(BatchSize);
        var total = 0;

        await foreach (var fields in ReadCsvAsync(path, expected: 5, cancellationToken))
        {
            var kind = fields[2];
            var bridges = int.Parse(fields[3], CultureInfo.InvariantCulture);

            if (kind is not ("different_county_same_state" or "different_state" or "county_not_published"))
            {
                throw new InvalidOperationException(
                    $"Disagreement '{fields[0]}'→'{fields[1]}' in {Path.GetFileName(path)}: unknown kind '{kind}'.");
            }

            if (bridges <= 0)
            {
                // Pairs are published only where something takes that path; a zero means the job's
                // grouping is wrong, and a QA page listing a disagreement nobody makes is noise.
                throw new InvalidOperationException(
                    $"Disagreement '{fields[0]}'→'{fields[1]}' in {Path.GetFileName(path)}: {bridges} bridges.");
            }

            batch.Add(new CountyJoinDisagreement
            {
                NbiCountyFips = NullIfEmpty(fields[0]),
                CountyFips = fields[1],
                Kind = kind,
                Bridges = bridges,
                NbiFipsInTiger = ParseNullableBool(fields[4]),
                CountyJoinRunId = run?.Id ?? 0,
            });
            total++;

            if (batch.Count >= BatchSize)
            {
                if (!dryRun)
                {
                    await UpsertDisagreementsAsync(batch, cancellationToken);
                }

                batch.Clear();
            }
        }

        if (!dryRun && batch.Count > 0)
        {
            await UpsertDisagreementsAsync(batch, cancellationToken);
        }

        return total;
    }

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    private static long? ParseNullableLong(string value) =>
        value.Length == 0 ? null : long.Parse(value, CultureInfo.InvariantCulture);

    private static short? ParseNullableShort(string value) =>
        value.Length == 0 ? null : short.Parse(value, CultureInfo.InvariantCulture);

    private static bool? ParseNullableBool(string value) => value.Length == 0
        ? null
        : value switch
        {
            "true" => true,
            "false" => false,
            _ => throw new InvalidOperationException($"'{value}' is not a boolean written by the join."),
        };

    /// <summary>Same shape as the FR-1.2/FR-1.3 loaders: arrays → <c>unnest</c> → upsert, one round trip per batch.</summary>
    private async Task UpsertCountiesAsync(IReadOnlyList<CensusCounty> batch, CancellationToken cancellationToken)
    {
        await using var command = await CreateCommandAsync(CountyUpsertSql, cancellationToken);
        AddArray(command, "county_fips", NpgsqlDbType.Text, batch.Select(c => (object)c.CountyFips));
        AddArray(command, "state_fips", NpgsqlDbType.Text, batch.Select(c => (object)c.StateFips));
        AddArray(command, "names", NpgsqlDbType.Text, batch.Select(c => (object)c.Name));
        AddArray(command, "names_lsad", NpgsqlDbType.Text, batch.Select(c => (object)c.NameLsad));
        AddArray(command, "land", NpgsqlDbType.Bigint, batch.Select(c => (object)c.LandAreaM2));
        AddArray(command, "water", NpgsqlDbType.Bigint, batch.Select(c => (object)c.WaterAreaM2));
        AddArray(command, "tiger_vintages", NpgsqlDbType.Smallint, batch.Select(c => (object)c.TigerVintage));
        AddArray(command, "populations", NpgsqlDbType.Bigint, batch.Select(c => c.Population as object ?? DBNull.Value));
        AddArray(command, "moes", NpgsqlDbType.Bigint, batch.Select(c => c.PopulationMoe as object ?? DBNull.Value));
        AddArray(command, "acs_vintages", NpgsqlDbType.Smallint, batch.Select(c => c.AcsVintage as object ?? DBNull.Value));
        AddArray(command, "acs_periods", NpgsqlDbType.Text, batch.Select(c => c.AcsPeriod as object ?? DBNull.Value));
        AddArray(command, "acs_tables", NpgsqlDbType.Text, batch.Select(c => c.AcsTable as object ?? DBNull.Value));
        AddArray(command, "run_ids", NpgsqlDbType.Bigint, batch.Select(c => (object)c.CountyJoinRunId));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task UpsertMissesAsync(IReadOnlyList<CountyJoinMiss> batch, CancellationToken cancellationToken)
    {
        await using var command = await CreateCommandAsync(MissUpsertSql, cancellationToken);
        AddArray(command, "state_codes", NpgsqlDbType.Text, batch.Select(m => (object)m.StateCode));
        AddArray(command, "structure_numbers", NpgsqlDbType.Text, batch.Select(m => (object)m.StructureNumber));
        AddArray(command, "record_types", NpgsqlDbType.Text, batch.Select(m => (object)m.RecordType));
        AddArray(command, "nbi_fips", NpgsqlDbType.Text, batch.Select(m => m.NbiCountyFips as object ?? DBNull.Value));
        AddArray(command, "reasons", NpgsqlDbType.Text, batch.Select(m => (object)m.Reason));
        AddArray(command, "touching", NpgsqlDbType.Integer, batch.Select(m => (object)m.TouchingCounties));
        AddArray(command, "nearest_fips", NpgsqlDbType.Text, batch.Select(m => m.NearestCountyFips as object ?? DBNull.Value));
        AddArray(command, "nearest_m", NpgsqlDbType.Bigint, batch.Select(m => m.NearestDistanceMeters as object ?? DBNull.Value));
        AddArray(command, "lons", NpgsqlDbType.Double, batch.Select(m => (object)m.Longitude));
        AddArray(command, "lats", NpgsqlDbType.Double, batch.Select(m => (object)m.Latitude));
        AddArray(command, "run_ids", NpgsqlDbType.Bigint, batch.Select(m => (object)m.CountyJoinRunId));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task UpsertDisagreementsAsync(
        IReadOnlyList<CountyJoinDisagreement> batch, CancellationToken cancellationToken)
    {
        await using var command = await CreateCommandAsync(DisagreementUpsertSql, cancellationToken);
        AddArray(command, "nbi_fips", NpgsqlDbType.Text, batch.Select(d => d.NbiCountyFips as object ?? DBNull.Value));
        AddArray(command, "county_fips", NpgsqlDbType.Text, batch.Select(d => (object)d.CountyFips));
        AddArray(command, "kinds", NpgsqlDbType.Text, batch.Select(d => (object)d.Kind));
        AddArray(command, "bridges", NpgsqlDbType.Integer, batch.Select(d => (object)d.Bridges));
        AddArray(command, "in_tiger", NpgsqlDbType.Boolean, batch.Select(d => d.NbiFipsInTiger as object ?? DBNull.Value));
        AddArray(command, "run_ids", NpgsqlDbType.Bigint, batch.Select(d => (object)d.CountyJoinRunId));
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

    private const string CountyUpsertSql = """
        INSERT INTO analytics.census_county (
            county_fips, state_fips, name, name_lsad, land_area_m2, water_area_m2, tiger_vintage,
            population, population_moe, acs_vintage, acs_period, acs_table, county_join_run_id)
        SELECT u.county_fips, u.state_fips, u.name, u.name_lsad, u.land, u.water, u.tiger_vintage,
               u.population, u.moe, u.acs_vintage, u.acs_period, u.acs_table, u.run_id
        FROM unnest(@county_fips, @state_fips, @names, @names_lsad, @land, @water, @tiger_vintages,
                    @populations, @moes, @acs_vintages, @acs_periods, @acs_tables, @run_ids)
        AS u(county_fips, state_fips, name, name_lsad, land, water, tiger_vintage,
             population, moe, acs_vintage, acs_period, acs_table, run_id)
        ON CONFLICT (county_fips) DO UPDATE SET
            state_fips = EXCLUDED.state_fips,
            name = EXCLUDED.name,
            name_lsad = EXCLUDED.name_lsad,
            land_area_m2 = EXCLUDED.land_area_m2,
            water_area_m2 = EXCLUDED.water_area_m2,
            tiger_vintage = EXCLUDED.tiger_vintage,
            population = EXCLUDED.population,
            population_moe = EXCLUDED.population_moe,
            acs_vintage = EXCLUDED.acs_vintage,
            acs_period = EXCLUDED.acs_period,
            acs_table = EXCLUDED.acs_table,
            county_join_run_id = EXCLUDED.county_join_run_id;
        """;

    private const string MissUpsertSql = """
        INSERT INTO analytics.county_join_miss (
            state_code, structure_number, record_type, nbi_county_fips, reason, touching_counties,
            nearest_county_fips, nearest_distance_meters, longitude, latitude, county_join_run_id)
        SELECT u.state_code, u.structure_number, u.record_type, u.nbi_fips, u.reason, u.touching,
               u.nearest_fips, u.nearest_m, u.lon, u.lat, u.run_id
        FROM unnest(@state_codes, @structure_numbers, @record_types, @nbi_fips, @reasons, @touching,
                    @nearest_fips, @nearest_m, @lons, @lats, @run_ids)
        AS u(state_code, structure_number, record_type, nbi_fips, reason, touching,
             nearest_fips, nearest_m, lon, lat, run_id)
        ON CONFLICT (state_code, structure_number, record_type) DO UPDATE SET
            nbi_county_fips = EXCLUDED.nbi_county_fips,
            reason = EXCLUDED.reason,
            touching_counties = EXCLUDED.touching_counties,
            nearest_county_fips = EXCLUDED.nearest_county_fips,
            nearest_distance_meters = EXCLUDED.nearest_distance_meters,
            longitude = EXCLUDED.longitude,
            latitude = EXCLUDED.latitude,
            county_join_run_id = EXCLUDED.county_join_run_id;
        """;

    private const string DisagreementUpsertSql = """
        INSERT INTO analytics.county_join_disagreement (
            nbi_county_fips, county_fips, kind, bridges, nbi_fips_in_tiger, county_join_run_id)
        SELECT u.nbi_fips, u.county_fips, u.kind, u.bridges, u.in_tiger, u.run_id
        FROM unnest(@nbi_fips, @county_fips, @kinds, @bridges, @in_tiger, @run_ids)
        AS u(nbi_fips, county_fips, kind, bridges, in_tiger, run_id)
        ON CONFLICT (nbi_county_fips, county_fips) DO UPDATE SET
            kind = EXCLUDED.kind,
            bridges = EXCLUDED.bridges,
            nbi_fips_in_tiger = EXCLUDED.nbi_fips_in_tiger,
            county_join_run_id = EXCLUDED.county_join_run_id;
        """;

    /// <summary>The subset of <c>manifest.json</c> the loader needs; the job writes more for humans.</summary>
    private sealed record CountyJoinManifest(
        string RunId,
        string CatalogSha256,
        string MethodVersion,
        string ContainmentPredicate,
        long Bridges,
        long Matched,
        long Unmatched,
        long Structures,
        long StructuresMatched,
        long Agree,
        long DifferentCountySameState,
        long DifferentState,
        long CountyNotPublished,
        int Counties,
        int CountiesWithoutPopulation,
        int Misses,
        int Disagreements,
        long DisagreementBridges);
}
