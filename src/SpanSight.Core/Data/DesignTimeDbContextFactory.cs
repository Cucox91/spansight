using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SpanSight.Core.Data;

/// <summary>
/// Design-time factory for <c>dotnet ef migrations</c>. The connection string targets the local
/// compose stack (docker-compose.yml) and is never used at runtime — cloud connections come from
/// configuration + managed identity (ADR-006-B).
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SpanSightDbContext>
{
    public SpanSightDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SpanSightDbContext>()
            .UseNpgsql(
                "Host=localhost;Port=5432;Database=spansight;Username=spansight;Password=spansight",
                npgsql => npgsql.UseNetTopologySuite())
            .Options;

        return new SpanSightDbContext(options);
    }
}
