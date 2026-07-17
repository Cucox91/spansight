namespace SpanSight.Ingestion;

/// <summary>Parsed command line. Kept dependency-free on purpose — three commands do not need a framework.</summary>
public sealed record CliOptions
{
    public required string Command { get; init; }

    public string? File { get; init; }

    public int? SnapshotYear { get; init; }

    public string? Connection { get; init; }

    public string? Output { get; init; }

    public bool Force { get; init; }

    public bool DryRun { get; init; }

    public int? Limit { get; init; }

    public const string Usage = """
        SpanSight ingestion CLI (FR-0.1/FR-0.2/FR-0.5)

        Usage:
          dotnet run --project src/SpanSight.Ingestion -- <command> [options]

        Commands:
          load             Parse, validate, and upsert an NBI delimited snapshot into core.
                             --file <path>           snapshot CSV (required)
                             --snapshot-year <yyyy>  vintage being loaded (required)
                             --dry-run               parse + validate only; no database writes
                             --force                 reload even if this exact file already completed
                             --limit <n>             stop after n data rows (smoke testing)
          export-geojson   Stream core bridges as GeoJSONSeq for the tile build (tools/build-tiles.sh).
                             --out <path>            output .geojsonl path (required)
          migrate          Apply EF Core migrations to the target database.

        Common options:
          --connection <cs>  overrides ConnectionStrings:SpanSight from configuration.
        """;

    public static (CliOptions? Options, string? Error) Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return (null, "No command given.");
        }

        var options = new CliOptions { Command = args[0].ToLowerInvariant() };
        if (options.Command is not ("load" or "export-geojson" or "migrate"))
        {
            return (null, $"Unknown command '{args[0]}'.");
        }

        for (var i = 1; i < args.Length; i++)
        {
            string? Next()
            {
                i++;
                return i < args.Length ? args[i] : null;
            }

            switch (args[i].ToLowerInvariant())
            {
                case "--file":
                    options = options with { File = Next() };
                    break;
                case "--snapshot-year":
                    if (!int.TryParse(Next(), out var year))
                    {
                        return (null, "--snapshot-year must be an integer (e.g. 2025).");
                    }

                    options = options with { SnapshotYear = year };
                    break;
                case "--connection":
                    options = options with { Connection = Next() };
                    break;
                case "--out":
                    options = options with { Output = Next() };
                    break;
                case "--force":
                    options = options with { Force = true };
                    break;
                case "--dry-run":
                    options = options with { DryRun = true };
                    break;
                case "--limit":
                    if (!int.TryParse(Next(), out var limit) || limit <= 0)
                    {
                        return (null, "--limit must be a positive integer.");
                    }

                    options = options with { Limit = limit };
                    break;
                default:
                    return (null, $"Unknown option '{args[i]}'.");
            }
        }

        return options.Command switch
        {
            "load" when options.File is null => (null, "load requires --file."),
            "load" when options.SnapshotYear is null => (null, "load requires --snapshot-year."),
            "export-geojson" when options.Output is null => (null, "export-geojson requires --out."),
            _ => (options, null),
        };
    }
}
