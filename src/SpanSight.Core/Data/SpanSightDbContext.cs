using System.Text;

using Microsoft.EntityFrameworkCore;

using SpanSight.Core.Analytics;
using SpanSight.Core.Domain;

namespace SpanSight.Core.Data;

/// <summary>
/// EF Core model for the serving database (ARCHITECTURE §4.1): canonical <c>core</c> schema,
/// <c>quarantine</c>, <c>ops</c> run bookkeeping, and the <c>analytics</c> aggregates published by
/// the offline DuckDB jobs (ADR-005 — compact aggregates only; the full history stays in Parquet).
/// Raw staging tables are created by the ingestion pipeline outside EF; the API only ever reads
/// <c>core</c> and <c>analytics</c>.
/// </summary>
public class SpanSightDbContext(DbContextOptions<SpanSightDbContext> options) : DbContext(options)
{
    public DbSet<Bridge> Bridges => Set<Bridge>();

    public DbSet<QuarantineRow> QuarantineRows => Set<QuarantineRow>();

    public DbSet<IngestionRun> IngestionRuns => Set<IngestionRun>();

    public DbSet<TrendRun> TrendRuns => Set<TrendRun>();

    public DbSet<BridgeConditionSeries> BridgeConditionSeries => Set<BridgeConditionSeries>();

    public DbSet<ConditionRollup> ConditionRollups => Set<ConditionRollup>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("postgis");

        modelBuilder.Entity<Bridge>(entity =>
        {
            entity.ToTable("bridge", "core");
            entity.HasKey(b => b.Id);
            entity.Property(b => b.StateCode).HasMaxLength(2);
            entity.Property(b => b.StructureNumber).HasMaxLength(32);
            entity.Property(b => b.RecordType).HasMaxLength(2);
            entity.Property(b => b.CountyCode).HasMaxLength(3);
            entity.Property(b => b.FeaturesIntersected).HasMaxLength(64);
            entity.Property(b => b.FacilityCarried).HasMaxLength(64);
            entity.Property(b => b.LocationText).HasMaxLength(64);
            entity.Property(b => b.MaterialCode).HasMaxLength(2);
            entity.Property(b => b.DesignCode).HasMaxLength(2);
            entity.Property(b => b.DeckCondition).HasMaxLength(1);
            entity.Property(b => b.SuperstructureCondition).HasMaxLength(1);
            entity.Property(b => b.SubstructureCondition).HasMaxLength(1);
            entity.Property(b => b.CulvertCondition).HasMaxLength(1);
            entity.Property(b => b.StructureLengthMeters).HasPrecision(9, 1);
            entity.Property(b => b.ConditionClass).HasConversion<string>().HasMaxLength(8);
            entity.Property(b => b.SourceFormat).HasConversion<string>().HasMaxLength(20);
            entity.Property(b => b.Location).HasColumnType("geometry(Point, 4326)");

            // Natural key: one serving row per structure (latest vintage upserts in place).
            entity.HasIndex(b => new { b.StateCode, b.StructureNumber, b.RecordType }).IsUnique();

            // Filter columns (FR-0.3) + spatial index for bbox queries.
            entity.HasIndex(b => b.Location).HasMethod("gist");
            entity.HasIndex(b => new { b.StateCode, b.CountyCode });
            entity.HasIndex(b => b.ConditionClass);
            entity.HasIndex(b => b.YearBuilt);
            entity.HasIndex(b => b.MaterialCode);
            entity.HasIndex(b => b.DesignCode);
            entity.HasIndex(b => b.Adt);
        });

        modelBuilder.Entity<QuarantineRow>(entity =>
        {
            entity.ToTable("quarantine_row", "quarantine");
            entity.HasKey(q => q.Id);
            entity.Property(q => q.StateCode).HasMaxLength(3);
            entity.Property(q => q.StructureNumber).HasMaxLength(32);
            entity.Property(q => q.Reasons).HasColumnType("text[]");
            entity.HasOne(q => q.IngestionRun).WithMany().HasForeignKey(q => q.IngestionRunId);
            entity.HasIndex(q => q.IngestionRunId);
            entity.HasIndex(q => q.StateCode);
        });

        modelBuilder.Entity<IngestionRun>(entity =>
        {
            entity.ToTable("ingestion_run", "ops");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.SourceFile).HasMaxLength(256);
            entity.Property(r => r.SourceSha256).HasMaxLength(64);
            entity.Property(r => r.Status).HasConversion<string>().HasMaxLength(16);
            entity.Property(r => r.Error).HasMaxLength(2048);
            entity.HasIndex(r => new { r.SourceSha256, r.SnapshotYear });
        });

        modelBuilder.Entity<TrendRun>(entity =>
        {
            entity.ToTable("trend_run", "analytics");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.JobRunId).HasMaxLength(64);
            entity.Property(r => r.CatalogSha256).HasMaxLength(64);
            entity.Property(r => r.Status).HasConversion<string>().HasMaxLength(16);
            entity.Property(r => r.Error).HasMaxLength(2048);
            entity.HasIndex(r => r.JobRunId).IsUnique();
        });

        modelBuilder.Entity<BridgeConditionSeries>(entity =>
        {
            entity.ToTable("bridge_condition_series", "analytics");
            entity.HasKey(s => s.Id);
            entity.Property(s => s.StateCode).HasMaxLength(2);
            entity.Property(s => s.StructureNumber).HasMaxLength(32);
            entity.Property(s => s.Ratings).HasMaxLength(ConditionSeriesCodec.MaxLength);
            entity.HasOne<TrendRun>().WithMany().HasForeignKey(s => s.TrendRunId);

            // The drawer's only shape: one structure by natural key. Covering the payload keeps it
            // an index-only scan, so the history fetch never touches the heap (NFR-1).
            entity.HasIndex(s => new { s.StateCode, s.StructureNumber })
                .IsUnique()
                .IncludeProperties(s => new { s.FirstYear, s.LastYear, s.ObservedYears, s.Ratings });

            // Convergence sweep after a load deletes by run; without this it is a full scan.
            entity.HasIndex(s => s.TrendRunId);
        });

        modelBuilder.Entity<ConditionRollup>(entity =>
        {
            entity.ToTable("condition_rollup", "analytics");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Level).HasConversion<string>().HasMaxLength(8);
            entity.Property(r => r.Fips).HasMaxLength(5);
            entity.HasOne<TrendRun>().WithMany().HasForeignKey(r => r.TrendRunId);

            // A trends query is (level, fips) over a year range — the year trails the key so the
            // range is a scan of contiguous index tuples rather than a filter.
            entity.HasIndex(r => new { r.Level, r.Fips, r.VintageYear }).IsUnique();
            entity.HasIndex(r => r.TrendRunId);
        });

        // Snake-case column names so hand-written SQL (staging merge, tile export, EXPLAIN
        // sessions) reads naturally alongside PostGIS functions.
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }
        }
    }

    internal static string ToSnakeCase(string name)
    {
        var builder = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0)
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
