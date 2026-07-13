namespace SpanSight.Api.Tests;

public class ScaffoldSmokeTests
{
    // TODO(raziel): Week 3 — [AI] builds the Testcontainers integration harness here;
    // [ME] writes the filter-correctness assertions (docs/IMPLEMENTATION-PLAN §5).
    [Fact]
    public void ApiAssembly_IsReferenced()
    {
        Assert.Equal("SpanSight.Api", typeof(Program).Assembly.GetName().Name);
    }
}
