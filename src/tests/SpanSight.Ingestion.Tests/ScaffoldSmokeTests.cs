namespace SpanSight.Ingestion.Tests;

public class ScaffoldSmokeTests
{
    // TODO(raziel): Week 2 — [AI] builds the test harness + fixtures (a few hundred rows max,
    // CLAUDE.md hard rule 4); [ME] writes the parser/validator assertions.
    [Fact]
    public void IngestionAssembly_IsReferenced()
    {
        Assert.Equal("SpanSight.Ingestion", typeof(Program).Assembly.GetName().Name);
    }
}
