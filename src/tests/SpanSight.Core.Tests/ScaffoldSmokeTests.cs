using SpanSight.Core.Geo;
using SpanSight.Core.Ingestion;

namespace SpanSight.Core.Tests;

public class ScaffoldSmokeTests
{
    // TODO(raziel): real assertions land with the Week 2 parser/validator work ([ME] chooses
    // the test cases per docs/AI-USAGE.md). This only proves solution wiring compiles.
    [Fact]
    public void CoreContracts_AreDefined()
    {
        Assert.True(typeof(INbiSnapshotParser).IsInterface);
        Assert.True(typeof(IDmsCoordinateConverter).IsInterface);
    }
}
