using System.Security.Cryptography;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Npgsql;

using NpgsqlTypes;

using SpanSight.Core.Data;
using SpanSight.Core.Domain;
using SpanSight.Core.Ingestion;
using SpanSight.Core.Ingestion.Validation;

namespace SpanSight.Ingestion;

public sealed record LoadSummary(long? RunId, int RowsRead, int RowsLoaded, int RowsQuarantined, bool Skipped);

/// <summary>
/// FR-0.1: idempotent, resumable snapshot load. Parse → validate → set-based upsert into
/// <c>core.bridge</c> (batched <c>unnest</c> + <c>ON CONFLICT</c> — no per-row round trips),
/// with rejects landing in <c>quarantine.quarantine_row</c> (FR-0.2) and a run-summary row in
/// <c>ops.ingestion_run</c>. Re-running a file that already completed is a logged no-op keyed on
/// the file's SHA-256 (AC-2); an interrupted run simply re-runs — the upsert converges (AC-3).
/// </summary>
public sealed class LoadPipeline(
    SpanSightDbContext db,
    INbiSnapshotParser parser,
    BridgeRowValidator validator,
    ILogger<LoadPipeline> logger)
{
    private const int BatchSize = 1000;

    public async Task<LoadSummary> RunAsync(
        string filePath,
        int snapshotYear,
        bool dryRun,
        bool force,
        int? limit,
        CancellationToken cancellationToken = default)
    {
        var fileName = Path.GetFileName(filePath);
        var sha256 = await ComputeSha256Async(filePath, cancellationToken);

        IngestionRun? run = null;
        if (!dryRun)
        {
            await db.Database.MigrateAsync(cancellationToken);

            var alreadyLoaded = await db.IngestionRuns.AnyAsync(
                r => r.SourceSha256 == sha256 && r.SnapshotYear == snapshotYear && r.Status == IngestionRunStatus.Completed,
                cancellationToken);
            if (alreadyLoaded && !force)
            {
                logger.LogInformation(
                    "Snapshot {File} (sha256 {Sha}) already completed for {Year}; no-op. Use --force to reload.",
                    fileName, sha256[..12], snapshotYear);
                return new LoadSummary(null, 0, 0, 0, Skipped: true);
            }

            run = new IngestionRun
            {
                StartedUtc = DateTimeOffset.UtcNow,
                SourceFile = fileName,
                SourceSha256 = sha256,
                SnapshotYear = snapshotYear,
                Status = IngestionRunStatus.Running,
            };
            db.IngestionRuns.Add(run);
            await db.SaveChangesAsync(cancellationToken);
        }

        var rowsRead = 0;
        var rowsLoaded = 0;
        var rowsQuarantined = 0;
        var seenKeys = new HashSet<(string State, string Number, string RecordType)>();
        var bridgeBatch = new List<Bridge>(BatchSize);
        var quarantineBatch = new List<QuarantineRow>(BatchSize);

        try
        {
            await using var stream = File.OpenRead(filePath);
            await foreach (var row in parser.ParseAsync(stream, cancellationToken))
            {
                rowsRead++;

                if (row.StructuralFault is not null)
                {
                    rowsQuarantined++;
                    quarantineBatch.Add(new QuarantineRow
                    {
                        IngestionRunId = run?.Id ?? 0,
                        LineNumber = row.LineNumber,
                        Reasons = [QuarantineReasons.StructuralFault],
                        RawLine = row.RawLine,
                    });
                }
                else
                {
                    var record = row.Record!;
                    var result = validator.Validate(record, snapshotYear);
                    if (result.IsValid && !seenKeys.Add((result.Bridge!.StateCode, result.Bridge.StructureNumber, result.Bridge.RecordType)))
                    {
                        result = new BridgeValidationResult { Reasons = [QuarantineReasons.DuplicateKey] };
                    }

                    if (result.IsValid)
                    {
                        rowsLoaded++;
                        bridgeBatch.Add(result.Bridge!);
                    }
                    else
                    {
                        rowsQuarantined++;
                        quarantineBatch.Add(new QuarantineRow
                        {
                            IngestionRunId = run?.Id ?? 0,
                            LineNumber = row.LineNumber,
                            StateCode = record.StateCode,
                            StructureNumber = record.StructureNumber,
                            Reasons = [.. result.Reasons],
                            RawLine = row.RawLine,
                        });
                    }
                }

                if (!dryRun && bridgeBatch.Count >= BatchSize)
                {
                    await UpsertBridgesAsync(bridgeBatch, cancellationToken);
                    bridgeBatch.Clear();
                }

                if (!dryRun && quarantineBatch.Count >= BatchSize)
                {
                    await FlushQuarantineAsync(quarantineBatch, cancellationToken);
                    quarantineBatch.Clear();
                }

                if (rowsRead % 50_000 == 0)
                {
                    logger.LogInformation("… {Rows:N0} rows read ({Loaded:N0} loaded, {Quarantined:N0} quarantined)", rowsRead, rowsLoaded, rowsQuarantined);
                }

                if (limit is { } max && rowsRead >= max)
                {
                    logger.LogInformation("--limit {Limit} reached; stopping early.", max);
                    break;
                }
            }

            if (!dryRun)
            {
                if (bridgeBatch.Count > 0)
                {
                    await UpsertBridgesAsync(bridgeBatch, cancellationToken);
                }

                if (quarantineBatch.Count > 0)
                {
                    await FlushQuarantineAsync(quarantineBatch, cancellationToken);
                }

                run!.CompletedUtc = DateTimeOffset.UtcNow;
                run.RowsRead = rowsRead;
                run.RowsLoaded = rowsLoaded;
                run.RowsQuarantined = rowsQuarantined;
                run.Status = IngestionRunStatus.Completed;
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex) when (run is not null)
        {
            run.Status = IngestionRunStatus.Failed;
            run.Error = ex.Message;
            run.RowsRead = rowsRead;
            run.RowsLoaded = rowsLoaded;
            run.RowsQuarantined = rowsQuarantined;
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        logger.LogInformation(
            "{Mode} complete: {Read:N0} read · {Loaded:N0} loaded · {Quarantined:N0} quarantined ({Pct:P2}).",
            dryRun ? "Dry run" : "Load", rowsRead, rowsLoaded, rowsQuarantined,
            rowsRead == 0 ? 0 : (double)rowsQuarantined / rowsRead);

        return new LoadSummary(run?.Id, rowsRead, rowsLoaded, rowsQuarantined, Skipped: false);
    }

    /// <summary>
    /// One round trip per batch: arrays → <c>unnest</c> → upsert. Geometry is assembled in SQL
    /// (<c>ST_SetSRID(ST_MakePoint(lon, lat), 4326)</c>) so the wire format stays primitive arrays.
    /// </summary>
    private async Task UpsertBridgesAsync(IReadOnlyList<Bridge> batch, CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = UpsertSql;

        AddArray(command, "state_codes", NpgsqlDbType.Text, batch, b => b.StateCode);
        AddArray(command, "structure_numbers", NpgsqlDbType.Text, batch, b => b.StructureNumber);
        AddArray(command, "record_types", NpgsqlDbType.Text, batch, b => b.RecordType);
        AddArray(command, "county_codes", NpgsqlDbType.Text, batch, b => b.CountyCode);
        AddArray(command, "features", NpgsqlDbType.Text, batch, b => b.FeaturesIntersected);
        AddArray(command, "facilities", NpgsqlDbType.Text, batch, b => b.FacilityCarried);
        AddArray(command, "location_texts", NpgsqlDbType.Text, batch, b => b.LocationText);
        AddArray(command, "lons", NpgsqlDbType.Double, batch, b => (object)b.Location.X);
        AddArray(command, "lats", NpgsqlDbType.Double, batch, b => (object)b.Location.Y);
        AddArray(command, "years_built", NpgsqlDbType.Integer, batch, b => (object?)b.YearBuilt);
        AddArray(command, "adts", NpgsqlDbType.Integer, batch, b => (object?)b.Adt);
        AddArray(command, "materials", NpgsqlDbType.Text, batch, b => b.MaterialCode);
        AddArray(command, "designs", NpgsqlDbType.Text, batch, b => b.DesignCode);
        AddArray(command, "lengths", NpgsqlDbType.Numeric, batch, b => (object?)b.StructureLengthMeters);
        AddArray(command, "decks", NpgsqlDbType.Text, batch, b => b.DeckCondition);
        AddArray(command, "supers", NpgsqlDbType.Text, batch, b => b.SuperstructureCondition);
        AddArray(command, "subs", NpgsqlDbType.Text, batch, b => b.SubstructureCondition);
        AddArray(command, "culverts", NpgsqlDbType.Text, batch, b => b.CulvertCondition);
        AddArray(command, "lowest_ratings", NpgsqlDbType.Integer, batch, b => (object?)b.LowestRating);
        AddArray(command, "condition_classes", NpgsqlDbType.Text, batch, b => b.ConditionClass.ToString());
        AddArray(command, "source_formats", NpgsqlDbType.Text, batch, b => b.SourceFormat.ToString());
        AddArray(command, "snapshot_years", NpgsqlDbType.Integer, batch, b => (object)b.SnapshotYear);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddArray<T>(
        NpgsqlCommand command,
        string name,
        NpgsqlDbType elementType,
        IReadOnlyList<Bridge> batch,
        Func<Bridge, T> selector)
    {
        var values = new object[batch.Count];
        for (var i = 0; i < batch.Count; i++)
        {
            object? value = selector(batch[i]);
            values[i] = value ?? DBNull.Value;
        }

        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Array | elementType) { Value = values });
    }

    private async Task FlushQuarantineAsync(IReadOnlyList<QuarantineRow> batch, CancellationToken cancellationToken)
    {
        db.QuarantineRows.AddRange(batch);
        await db.SaveChangesAsync(cancellationToken);

        // Detach only the flushed rows — a blanket ChangeTracker.Clear() would also detach the
        // run entity and silently drop its final Completed/Failed status update.
        foreach (var entry in db.ChangeTracker.Entries<QuarantineRow>().ToList())
        {
            entry.State = Microsoft.EntityFrameworkCore.EntityState.Detached;
        }
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }

    private const string UpsertSql = """
        INSERT INTO core.bridge (
            state_code, structure_number, record_type, county_code, features_intersected,
            facility_carried, location_text, location, year_built, adt, material_code,
            design_code, structure_length_meters, deck_condition, superstructure_condition,
            substructure_condition, culvert_condition, lowest_rating, condition_class,
            source_format, snapshot_year)
        SELECT
            u.state_code, u.structure_number, u.record_type, u.county_code, u.features,
            u.facility, u.location_text, ST_SetSRID(ST_MakePoint(u.lon, u.lat), 4326),
            u.year_built, u.adt, u.material, u.design, u.length_m, u.deck, u.super, u.sub,
            u.culvert, u.lowest_rating, u.condition_class, u.source_format, u.snapshot_year
        FROM unnest(
            @state_codes, @structure_numbers, @record_types, @county_codes, @features,
            @facilities, @location_texts, @lons, @lats, @years_built, @adts, @materials,
            @designs, @lengths, @decks, @supers, @subs, @culverts, @lowest_ratings,
            @condition_classes, @source_formats, @snapshot_years)
        AS u(
            state_code, structure_number, record_type, county_code, features, facility,
            location_text, lon, lat, year_built, adt, material, design, length_m, deck,
            super, sub, culvert, lowest_rating, condition_class, source_format, snapshot_year)
        ON CONFLICT (state_code, structure_number, record_type) DO UPDATE SET
            county_code = EXCLUDED.county_code,
            features_intersected = EXCLUDED.features_intersected,
            facility_carried = EXCLUDED.facility_carried,
            location_text = EXCLUDED.location_text,
            location = EXCLUDED.location,
            year_built = EXCLUDED.year_built,
            adt = EXCLUDED.adt,
            material_code = EXCLUDED.material_code,
            design_code = EXCLUDED.design_code,
            structure_length_meters = EXCLUDED.structure_length_meters,
            deck_condition = EXCLUDED.deck_condition,
            superstructure_condition = EXCLUDED.superstructure_condition,
            substructure_condition = EXCLUDED.substructure_condition,
            culvert_condition = EXCLUDED.culvert_condition,
            lowest_rating = EXCLUDED.lowest_rating,
            condition_class = EXCLUDED.condition_class,
            source_format = EXCLUDED.source_format,
            snapshot_year = EXCLUDED.snapshot_year;
        """;
}
