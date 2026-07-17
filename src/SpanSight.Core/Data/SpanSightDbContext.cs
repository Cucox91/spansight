using System.Text;

using Microsoft.EntityFrameworkCore;

using SpanSight.Core.Domain;

namespace SpanSight.Core.Data;

/// <summary>
/// EF Core model for the serving database (ARCHITECTURE §4.1): canonical <c>core</c> schema,
/// <c>quarantine</c>, and <c>ops</c> run bookkeeping. Raw staging tables are created by the
/// ingestion pipeline outside EF; the API and analytics only ever read <c>core</c>.
/// </summary>
public class SpanSightDbContext(DbContextOptions<SpanSightDbContext> options) : DbContext(options)
{
    public DbSet<Bridge> Bridges => Set<Bridge>();

    public DbSet<QuarantineRow> QuarantineRows => Set<QuarantineRow>();

    public DbSet<IngestionRun> IngestionRuns => Set<IngestionRun>();

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
