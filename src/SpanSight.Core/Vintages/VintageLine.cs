namespace SpanSight.Core.Vintages;

/// <summary>
/// Field splitting for NBI vintage files.
/// <para>
/// FHWA documents these exports as "comma separated, and the text qualifier is a single quote",
/// but the published data does not honour its own dialect: apostrophes appear unescaped inside
/// qualified text (<c>'O'NEAL ROAD'</c> in 1992, <c>'MOORE'S MILL CREEK'</c> in 2010). Treating the
/// single quote as a real quote character therefore mis-splits those rows — DuckDB's CSV sniffer
/// refuses the 1992 file outright for exactly this reason.
/// </para>
/// <para>
/// So the qualifier is treated as decoration, not structure: split on every comma, then strip a
/// balanced pair of surrounding quotes. That is only safe if no field value ever contains a comma,
/// which was checked across all 1,894,892 data rows of the 1992, 2010 and 2025 national files —
/// zero rows disagreed with their header's field count. Should a future vintage break that, the
/// row's field count stops matching and it is rejected with
/// <see cref="VintageRejectReasons.FieldCountMismatch"/> rather than silently mis-parsed.
/// </para>
/// <para>
/// This is deliberately <i>not</i> <c>DelimitedLineSplitter</c>: that one implements RFC 4180
/// double-quote semantics for the Phase 0 serving path, which is a different and correct job.
/// </para>
/// </summary>
public static class VintageLine
{
    public static string[] Split(string line)
    {
        var fields = line.Split(',');
        for (var i = 0; i < fields.Length; i++)
        {
            fields[i] = Clean(fields[i]);
        }

        return fields;
    }

    /// <summary>
    /// Strips the single-quote text qualifier and the fixed-width padding the older eras keep
    /// inside it (<c>'BUCK CREEK              '</c> → <c>BUCK CREEK</c>), so a value compares equal
    /// across vintages regardless of which era produced it.
    /// </summary>
    public static string Clean(string field)
    {
        var value = field.Trim();
        if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
        {
            value = value[1..^1];
        }

        return value.Trim();
    }
}
