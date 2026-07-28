using System.Globalization;
using System.Text;

namespace SpanSight.Api.Export;

/// <summary>
/// Server-generated CSV for the FR-1.4 ranking and report-card views (AC-3).
/// <para>
/// Server-side rather than assembled in the browser for three reasons the AC implies: the export
/// then carries the same definition text the view is required to display, it goes through the same
/// rate limiter as every other <c>/api</c> route, and it is a fixed artefact that golden-file tests
/// can compare byte for byte — a Blob built from whatever the SPA happened to have in state is not.
/// </para>
/// <para>
/// <b>Every byte here is deterministic.</b> Line endings are LF regardless of host, numbers are
/// formatted with <see cref="CultureInfo.InvariantCulture"/> so a machine with a comma decimal
/// separator cannot emit a different file, and nothing carries a timestamp or a wall-clock value.
/// A golden file that churns is a golden file that gets deleted.
/// </para>
/// </summary>
public sealed class CsvExport
{
    /// <summary>
    /// RFC 4180 says nothing about comments, but a leading <c>#</c> block is what every reader that
    /// supports them expects, and DuckDB, pandas and R all skip it on request.
    /// <para>
    /// The definition goes in the file rather than only on the page because a CSV is the copy that
    /// leaves the building. GR-6 asks that the method travel with the numbers, and a ranking whose
    /// exclusion rule stayed behind in the browser is exactly the bypass FR-1.3's cadence caption
    /// turned out to be.
    /// </para>
    /// </summary>
    public const char CommentPrefix = '#';

    private readonly StringBuilder _builder = new();

    public CsvExport Comment(string text)
    {
        // Wrapped nowhere and escaped nowhere: a stray newline inside a comment would produce a
        // line the reader cannot skip, so they are collapsed rather than trusted.
        _builder.Append(CommentPrefix).Append(' ')
            .Append(text.ReplaceLineEndings(" ").Trim())
            .Append('\n');
        return this;
    }

    public CsvExport Comment(string label, string value) => Comment($"{label}: {value}");

    public CsvExport Header(params string[] columns) => Row(columns);

    public CsvExport Row(params string?[] values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            if (i > 0)
            {
                _builder.Append(',');
            }

            _builder.Append(Escape(values[i]));
        }

        _builder.Append('\n');
        return this;
    }

    /// <summary>
    /// Invariant integer formatting with no group separators — <c>1234</c>, never <c>1,234</c>,
    /// which would silently become two columns.
    /// </summary>
    public static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Number(int? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>Also the overload a plain <c>long</c> binds to, which is why there is no non-nullable one.</summary>
    public static string Number(long? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>
    /// A percentage to one decimal, or empty where none was published. Empty rather than <c>0</c>:
    /// a share that could not be computed and a share that is genuinely zero are different facts,
    /// and a spreadsheet will average them together if they look the same (GR-6).
    /// </summary>
    public static string Percent(double? value) =>
        value?.ToString("0.0", CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>RFC 4180: quote when the value holds a comma, a quote or a newline; double the quotes.</summary>
    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        // Newlines are replaced, not quoted. A quoted embedded newline is legal RFC 4180 and is
        // still the single most common way a downstream reader mis-parses a file — and the only
        // free-text this API exports is NBI items 6A/7, which are published as one line each.
        var flattened = value.ReplaceLineEndings(" ");

        return flattened.AsSpan().IndexOfAny(',', '"', '\n') >= 0
            ? $"\"{flattened.Replace("\"", "\"\"")}\""
            : flattened;
    }

    public override string ToString() => _builder.ToString();

    /// <summary>
    /// The download response. UTF-8 without a BOM: the payload is ASCII apart from county names
    /// such as Doña Ana, and a BOM would corrupt the first header cell for readers that do not
    /// strip it.
    /// </summary>
    public IResult ToResult(string fileName) =>
        Results.File(Encoding.UTF8.GetBytes(ToString()), "text/csv; charset=utf-8", fileName);
}
