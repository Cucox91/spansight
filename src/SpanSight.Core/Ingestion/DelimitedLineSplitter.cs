namespace SpanSight.Core.Ingestion;

/// <summary>
/// Minimal RFC 4180-style field splitter for NBI delimited files: comma separators, optional
/// double-quoted fields, doubled quotes as escapes. Newlines inside quoted fields are not
/// supported — the published NBI exports do not use them, and treating every physical line as
/// one record keeps line numbers meaningful for the quarantine table.
/// </summary>
public static class DelimitedLineSplitter
{
    public static string[] Split(ReadOnlySpan<char> line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"' && current.Length == 0)
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return [.. fields];
    }
}
