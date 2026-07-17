using System.Runtime.CompilerServices;

namespace SpanSight.Core.Ingestion;

/// <inheritdoc cref="INbiSnapshotParser"/>
public sealed class NbiSnapshotParser : INbiSnapshotParser
{
    public async IAsyncEnumerable<NbiRowResult> ParseAsync(
        Stream snapshot,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(snapshot, leaveOpen: true);

        var headerLine = await reader.ReadLineAsync(cancellationToken)
            ?? throw new NbiFormatException("Snapshot file is empty — no header row.");

        var columns = BuildColumnIndex(headerLine);
        var missing = NbiColumns.Required.Where(c => !columns.ContainsKey(c)).ToList();
        if (missing.Count > 0)
        {
            throw new NbiFormatException(
                $"Snapshot header is missing required column(s): {string.Join(", ", missing)}. " +
                "Verify the file is an FHWA NBI delimited snapshot (Coding Guide era).");
        }

        var lineNumber = 1; // header consumed
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            lineNumber++;
            if (line.Length == 0)
            {
                continue; // trailing blank lines are common; not worth quarantining
            }

            var fields = DelimitedLineSplitter.Split(line);
            if (fields.Length != columns.Count)
            {
                yield return new NbiRowResult
                {
                    LineNumber = lineNumber,
                    RawLine = line,
                    StructuralFault =
                        $"Field count {fields.Length} does not match header count {columns.Count}.",
                };
                continue;
            }

            string? Get(string column) =>
                columns.TryGetValue(column, out var index) && fields[index].Trim() is { Length: > 0 } value
                    ? value
                    : null;

            var stateCode = Get(NbiColumns.StateCode);
            var structureNumber = Get(NbiColumns.StructureNumber);
            if (stateCode is null || structureNumber is null)
            {
                yield return new NbiRowResult
                {
                    LineNumber = lineNumber,
                    RawLine = line,
                    StructuralFault = "Blank key field (state code and/or structure number).",
                };
                continue;
            }

            yield return new NbiRowResult
            {
                LineNumber = lineNumber,
                RawLine = line,
                Record = new NbiRawRecord
                {
                    StateCode = stateCode,
                    StructureNumber = structureNumber,
                    RecordType = Get(NbiColumns.RecordType) ?? "1",
                    CountyCode = Get(NbiColumns.CountyCode),
                    FeaturesDescription = Get(NbiColumns.FeaturesDescription),
                    FacilityCarried = Get(NbiColumns.FacilityCarried),
                    LocationText = Get(NbiColumns.Location),
                    RawLatitude = Get(NbiColumns.Latitude),
                    RawLongitude = Get(NbiColumns.Longitude),
                    RawYearBuilt = Get(NbiColumns.YearBuilt),
                    RawAdt = Get(NbiColumns.Adt),
                    MaterialCode = Get(NbiColumns.StructureKind),
                    DesignCode = Get(NbiColumns.StructureType),
                    RawStructureLengthMeters = Get(NbiColumns.StructureLengthMeters),
                    DeckCondition = Get(NbiColumns.DeckCondition),
                    SuperstructureCondition = Get(NbiColumns.SuperstructureCondition),
                    SubstructureCondition = Get(NbiColumns.SubstructureCondition),
                    CulvertCondition = Get(NbiColumns.CulvertCondition),
                },
            };
        }
    }

    private static Dictionary<string, int> BuildColumnIndex(string headerLine)
    {
        var headers = DelimitedLineSplitter.Split(headerLine);
        var index = new Dictionary<string, int>(headers.Length, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Length; i++)
        {
            index.TryAdd(headers[i].Trim(), i); // first occurrence wins on duplicate headers
        }

        return index;
    }
}
