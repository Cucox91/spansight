using SpanSight.Core.Ingestion;

using Xunit;

namespace SpanSight.Core.Tests;

public class DelimitedLineSplitterTests
{
    [Fact]
    public void Splits_plain_fields()
    {
        Assert.Equal(["a", "b", "c"], DelimitedLineSplitter.Split("a,b,c"));
    }

    [Fact]
    public void Preserves_empty_fields()
    {
        Assert.Equal(["a", "", "c", ""], DelimitedLineSplitter.Split("a,,c,"));
    }

    [Fact]
    public void Quoted_field_keeps_embedded_comma()
    {
        Assert.Equal(["12", "MIAMI RIVER, NORTH FORK", "x"], DelimitedLineSplitter.Split("12,\"MIAMI RIVER, NORTH FORK\",x"));
    }

    [Fact]
    public void Doubled_quotes_escape_a_quote()
    {
        Assert.Equal(["SAY \"HI\"", "y"], DelimitedLineSplitter.Split("\"SAY \"\"HI\"\"\",y"));
    }

    [Fact]
    public void Single_field_line_yields_one_field()
    {
        Assert.Equal(["only"], DelimitedLineSplitter.Split("only"));
    }
}
