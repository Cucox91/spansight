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

    public DbSet<DeteriorationRun> DeteriorationRuns => Set<DeteriorationRun>();

    public DbSet<DeteriorationMatrixRow> DeteriorationMatrixRows => Set<DeteriorationMatrixRow>();

    public DbSet<DeteriorationMatrixCell> DeteriorationMatrixCells => Set<DeteriorationMatrixCell>();

    public DbSet<CountyJoinRun> CountyJoinRuns => Set<CountyJoinRun>();

    public DbSet<CensusCounty> CensusCounties => Set<CensusCounty>();

    public DbSet<CountyJoinMiss> CountyJoinMisses => Set<CountyJoinMiss>();

    public DbSet<CountyJoinDisagreement> CountyJoinDisagreements => Set<CountyJoinDisagreement>();

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

            // FR-1.4: the rankings group every record-type-1 row by state, by county, or by the
            // published 43A/43B codes, and read only the condition class off it. Covering all five
            // makes each of those an index-only scan — measured 40 ms for 623k rows against 74 ms
            // on a parallel sequential scan, with zero heap fetches, which is what keeps the shape
            // inside NFR-1 on a single-vCPU B1ms where there are no parallel workers to help.
            entity.HasIndex(b => b.RecordType)
                .HasDatabaseName("ix_bridge_ranking")
                .IncludeProperties(b => new
                {
                    b.StateCode,
                    b.CountyCode,
                    b.DesignCode,
                    b.MaterialCode,
                    b.ConditionClass,
                });

            // Every ranking and every report card asks the table which snapshot it is serving —
            // `ORDER BY snapshot_year DESC LIMIT 1` — before it does anything else. Unindexed that
            // is a parallel sequential scan of all 741k rows: measured at 38-53 ms on the dev Mac,
            // where it was the single largest cost in both endpoints (the report card's own four
            // queries total 1.1 ms). A B1ms has one vCPU and no parallel workers to hide it.
            //
            // The column holds one distinct value today, which is why this index looks pointless
            // and is not: the schema permits a database holding more than one loaded snapshot, the
            // endpoints ask for the newest, and an index is what makes "newest" a lookup instead of
            // a scan. Postgres reads it backwards for the DESC order (NFR-1).
            entity.HasIndex(b => b.SnapshotYear);
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

        // FR-1.3 — the deterioration matrices. A separate run table from FR-1.2 on purpose: the two
        // jobs are published and rolled back independently (RUNBOOK §10.3/§10.4).
        modelBuilder.Entity<DeteriorationRun>(entity =>
        {
            entity.ToTable("deterioration_run", "analytics");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.JobRunId).HasMaxLength(64);
            entity.Property(r => r.CatalogSha256).HasMaxLength(64);
            entity.Property(r => r.MethodologyVersion).HasMaxLength(16);
            entity.Property(r => r.Status).HasConversion<string>().HasMaxLength(16);
            entity.Property(r => r.Error).HasMaxLength(2048);
            entity.HasIndex(r => r.JobRunId).IsUnique();
        });

        modelBuilder.Entity<DeteriorationMatrixRow>(entity =>
        {
            entity.ToTable("deterioration_matrix_row", "analytics");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Component).HasConversion<string>().HasMaxLength(16);
            entity.Property(r => r.TypeGroup).HasMaxLength(24);
            entity.Property(r => r.MaterialGroup).HasMaxLength(32);
            entity.Property(r => r.Region).HasMaxLength(32);
            entity.HasOne<DeteriorationRun>().WithMany().HasForeignKey(r => r.DeteriorationRunId);

            // The API's only shape: one cohort's whole matrix for one component — ten rows read by
            // the leading four key columns, so the from-rating trails the key.
            entity.HasIndex(r => new { r.Component, r.TypeGroup, r.MaterialGroup, r.Region, r.FromRating })
                .IsUnique();
            entity.HasIndex(r => r.DeteriorationRunId);
        });

        modelBuilder.Entity<DeteriorationMatrixCell>(entity =>
        {
            entity.ToTable("deterioration_matrix_cell", "analytics");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Component).HasConversion<string>().HasMaxLength(16);
            entity.Property(c => c.TypeGroup).HasMaxLength(24);
            entity.Property(c => c.MaterialGroup).HasMaxLength(32);
            entity.Property(c => c.Region).HasMaxLength(32);
            entity.HasOne<DeteriorationRun>().WithMany().HasForeignKey(c => c.DeteriorationRunId);

            entity.HasIndex(c => new { c.Component, c.TypeGroup, c.MaterialGroup, c.Region, c.FromRating, c.ToRating })
                .IsUnique();
            entity.HasIndex(c => c.DeteriorationRunId);
        });

        // FR-1.5 — the census join. A third independent run table for the third offline job, so the
        // county join publishes and rolls back without touching the trend series or the matrices
        // (RUNBOOK §10.5).
        modelBuilder.Entity<CountyJoinRun>(entity =>
        {
            entity.ToTable("county_join_run", "analytics");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.JobRunId).HasMaxLength(64);
            entity.Property(r => r.CatalogSha256).HasMaxLength(64);
            entity.Property(r => r.MethodVersion).HasMaxLength(16);
            entity.Property(r => r.ContainmentPredicate).HasMaxLength(32);
            entity.Property(r => r.Status).HasConversion<string>().HasMaxLength(16);
            entity.Property(r => r.Error).HasMaxLength(2048);
            entity.HasIndex(r => r.JobRunId).IsUnique();
        });

        modelBuilder.Entity<CensusCounty>(entity =>
        {
            entity.ToTable("census_county", "analytics");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.CountyFips).HasMaxLength(5);
            entity.Property(c => c.StateFips).HasMaxLength(2);
            entity.Property(c => c.Name).HasMaxLength(100);
            entity.Property(c => c.NameLsad).HasMaxLength(120);
            entity.Property(c => c.AcsPeriod).HasMaxLength(16);
            entity.Property(c => c.AcsTable).HasMaxLength(16);
            entity.HasOne<CountyJoinRun>().WithMany().HasForeignKey(c => c.CountyJoinRunId);

            // Every county lookup is by FIPS, and the report card wants the whole row, so the payload
            // is covered: a name and a population never touch the heap (NFR-1).
            entity.HasIndex(c => c.CountyFips)
                .IsUnique()
                .IncludeProperties(c => new
                {
                    c.StateFips,
                    c.Name,
                    c.NameLsad,
                    c.Population,
                    c.PopulationMoe,
                    c.AcsVintage,
                    c.AcsPeriod,
                    c.TigerVintage,
                });
            entity.HasIndex(c => c.StateFips);
            entity.HasIndex(c => c.CountyJoinRunId);
        });

        modelBuilder.Entity<CountyJoinMiss>(entity =>
        {
            entity.ToTable("county_join_miss", "analytics");
            entity.HasKey(m => m.Id);
            entity.Property(m => m.StateCode).HasMaxLength(2);
            entity.Property(m => m.StructureNumber).HasMaxLength(32);
            entity.Property(m => m.RecordType).HasMaxLength(2);
            entity.Property(m => m.NbiCountyFips).HasMaxLength(5);
            entity.Property(m => m.Reason).HasMaxLength(32);
            entity.Property(m => m.NearestCountyFips).HasMaxLength(5);
            entity.HasOne<CountyJoinRun>().WithMany().HasForeignKey(m => m.CountyJoinRunId);

            entity.HasIndex(m => new { m.StateCode, m.StructureNumber, m.RecordType }).IsUnique();
            entity.HasIndex(m => m.CountyJoinRunId);
        });

        modelBuilder.Entity<CountyJoinDisagreement>(entity =>
        {
            entity.ToTable("county_join_disagreement", "analytics");
            entity.HasKey(d => d.Id);
            entity.Property(d => d.NbiCountyFips).HasMaxLength(5);
            entity.Property(d => d.CountyFips).HasMaxLength(5);
            entity.Property(d => d.Kind).HasMaxLength(32);
            entity.HasOne<CountyJoinRun>().WithMany().HasForeignKey(d => d.CountyJoinRunId);

            // NULLS NOT DISTINCT, because the natural key's first column is null for exactly one kind
            // — county_not_published, where item 3 carried no code. Postgres treats nulls as distinct
            // by default, so without this the unique index would not constrain those rows and the
            // loader's ON CONFLICT would insert a duplicate on every re-run instead of updating.
            entity.HasIndex(d => new { d.NbiCountyFips, d.CountyFips })
                .IsUnique()
                .AreNullsDistinct(false);
            entity.HasIndex(d => d.CountyJoinRunId);
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
