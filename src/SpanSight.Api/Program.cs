var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapOpenApi();
app.MapHealthChecks("/healthz");

// TODO(raziel): Week 3 [ME] — first endpoint GET /api/bridges (state/county/condition/type/year/
// bbox filters + pagination) with EF Core + NetTopologySuite mapping. First-of-a-kind, hand-written
// per docs/AI-USAGE.md; AI adds the remaining endpoints only after this lands.

app.Run();

public partial class Program;
