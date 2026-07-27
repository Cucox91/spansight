using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

using SpanSight.Core.Analytics;
using SpanSight.Core.Data;
using SpanSight.Core.Domain.Lookups;

namespace SpanSight.Api.Endpoints;

/// <summary>
/// FR-1.3 AC-1/AC-4 — cohort transition matrices over the <c>analytics</c> aggregates published by
/// <c>tools/deterioration/build-deterioration.sh</c>.
/// <para>
/// Everything served here is a count of how often published NBI condition ratings moved between
/// consecutive annual publications, for a cohort of bridges. Nothing is predicted, no chain is
/// iterated, no steady state is solved, and <b>no route accepts or returns a structure identifier</b>
/// — there is no per-structure deterioration surface anywhere in the product (GR-6, methodology §7).
/// </para>
/// </summary>
public static class DeteriorationEndpoints
{
    /// <summary>
    /// Stated next to every matrix the API returns and rendered in the UI beside the grid, so the
    /// method travels with the numbers rather than living only in a doc (GR-6, FR-1.3 AC-4).
    /// </summary>
    public const string MethodNoteTemplate =
        "Counts of how often bridges in this cohort were published at one NBI condition rating in a " +
        "given year and another the next, pooled across{0}. Ratings are " +
        "items 58 (deck), 59 (superstructure), 60 (substructure) and 62 (culvert), each its own " +
        "matrix; a year FHWA did not publish is a gap and is never bridged, and rating improvements " +
        "are kept exactly as published. A descriptive history of published inspection ratings at " +
        "cohort level — not a prediction for any structure, and not engineering advice.";

    /// <summary>
    /// The method note for the run being served. The vintage range comes from the run rather than
    /// being hardcoded: a job published over part of the catalog — a re-run limited to recent
    /// vintages, a rollback to an older job, or the CI fixture, which is 2020–2023 — would otherwise
    /// ship a claim of 34 pooled vintages next to provenance saying three (FR-1.3 AC-4).
    /// </summary>
    public static string MethodNote(DeteriorationRun? run) => string.Format(
        System.Globalization.CultureInfo.InvariantCulture,
        MethodNoteTemplate,
        run is null ? " the published consecutive vintages" : $" consecutive vintages {run.FirstYear}–{run.LastYear}");

    /// <summary>
    /// The methodology §6.1 caveat. Rendered adjacent to every matrix, carrying that matrix's own
    /// measured unchanged share rather than a generic hand-wave.
    /// </summary>
    public const string CadenceCaptionTemplate =
        "{0} of the transitions in rows above the sample-size floor show no change. NBI structures are " +
        "typically re-inspected on a ~24-month cycle and intermediate annual files carry the last " +
        "inspection forward, so many \"no change\" observations reflect no new inspection rather than " +
        "measured stability — which means the off-diagonal figures understate true annual change.";

    /// <summary>
    /// The same caveat for a matrix with no row above the floor. The share is itself a rate, so it is
    /// suppressed with the others rather than being published from evidence the response just
    /// declined to rate (FR-1.3 AC-3).
    /// </summary>
    public const string CadenceCaptionWithoutShare =
        "No row here holds enough transitions to publish a rate, so no share is stated. NBI structures " +
        "are typically re-inspected on a ~24-month cycle and intermediate annual files carry the last " +
        "inspection forward, so many \"no change\" observations reflect no new inspection rather than " +
        "measured stability — which means off-diagonal movement is understated wherever it is shown.";

    /// <summary>Where the published method lives, served with every response so a view cannot omit it.</summary>
    public const string MethodologyUrl =
        "https://github.com/Cucox91/spansight/blob/main/docs/METHODOLOGY-DETERIORATION.md";

    /// <summary>Used only when no run has been published yet, so the endpoints still describe themselves.</summary>
    private const int DefaultSampleFloor = 50;

    public static RouteGroupBuilder MapDeterioration(this RouteGroupBuilder group)
    {
        group.MapGet("/deterioration/cohorts", CatalogAsync)
            .WithSummary("Cohorts with published transition matrices")
            .WithDescription(
                "The cohort inventory a patterns view picks from — structure type group (item 43B) × " +
                "material group (item 43A) × NOAA climate region — with the pair count and the number " +
                "of above-floor rows in each of the four component families, plus the vocabulary of " +
                "every dimension and the published codes each group collapses. Cohort-level only: no " +
                "structure is identified anywhere in this API.");

        group.MapGet("/deterioration/matrix", MatrixAsync)
            .WithSummary("One cohort's transition matrix for one component")
            .WithDescription(
                "A 10×10 count of published rating transitions. Omit the cohort parameters for the " +
                "national all-cohorts context matrix, which is the exact sum of every cohort. Rows " +
                "below the published sample-size floor return their counts with sufficient=false and " +
                "null rates — the API never emits a rate the evidence cannot support. Every row " +
                "carries the span of years its evidence comes from, because all 33 year-pairs are " +
                "pooled. Descriptive statistics of published ratings; not a prediction and not " +
                "engineering advice.");

        return group;
    }

    private static async Task<Ok<DeteriorationCatalogDto>> CatalogAsync(
        SpanSightDbContext db,
        CancellationToken cancellationToken = default)
    {
        var run = await ServingRunAsync(db, preferredRunId: null, cancellationToken);

        var grouped = await db.DeteriorationMatrixRows.AsNoTracking()
            .GroupBy(r => new { r.Component, r.TypeGroup, r.MaterialGroup, r.Region })
            .Select(g => new
            {
                g.Key.Component,
                g.Key.TypeGroup,
                g.Key.MaterialGroup,
                g.Key.Region,
                Pairs = g.Sum(r => r.RowTotal),
                Rows = g.Count(),
                // The floor lives on the run row, so it is compared here rather than hardcoded.
                AboveFloor = g.Count(r => r.RowTotal >= (run == null ? DefaultSampleFloor : run.SampleFloor)),
            })
            .ToListAsync(cancellationToken);

        var cohorts = grouped
            .GroupBy(x => new { x.TypeGroup, x.MaterialGroup, x.Region })
            .Select(g => new DeteriorationCohortDto(
                g.Key.TypeGroup,
                g.Key.MaterialGroup,
                g.Key.Region,
                NbiCohorts.IsAllCohorts(g.Key.TypeGroup),
                [.. g.OrderBy(c => c.Component)
                     .Select(c => new CohortComponentDto(c.Component.ToString(), c.Pairs, c.Rows, c.AboveFloor))]))
            // The national context matrix first, then the largest cohorts — a picker should open on
            // something populated rather than on whichever group sorts first alphabetically.
            .OrderByDescending(c => c.IsNational)
            .ThenByDescending(c => c.Components.Sum(x => x.Pairs))
            .ToList();

        return TypedResults.Ok(new DeteriorationCatalogDto(
            Dimensions,
            cohorts,
            run?.SampleFloor ?? DefaultSampleFloor,
            MethodNote(run),
            run?.MethodologyVersion ?? "unpublished",
            MethodologyUrl,
            Provenance(run)));
    }

    private static async Task<Results<Ok<DeteriorationMatrixDto>, ValidationProblem>> MatrixAsync(
        SpanSightDbContext db,
        string? component,
        string? typeGroup,
        string? materialGroup,
        string? region,
        CancellationToken cancellationToken = default)
    {
        if (!TryBuildQuery(component, typeGroup, materialGroup, region, out var query, out var errors))
        {
            return TypedResults.ValidationProblem(errors);
        }

        var rows = await db.DeteriorationMatrixRows.AsNoTracking()
            .Where(r => r.Component == query.Component
                     && r.TypeGroup == query.TypeGroup
                     && r.MaterialGroup == query.MaterialGroup
                     && r.Region == query.Region)
            .ToListAsync(cancellationToken);

        // The run comes from the rows being returned, so the floor applied is the floor they were
        // computed under. An empty cohort has no row to ask, and falls back to the serving run.
        var run = await ServingRunAsync(
            db, rows.Count > 0 ? rows[0].DeteriorationRunId : null, cancellationToken);
        var floor = run?.SampleFloor ?? DefaultSampleFloor;

        var cells = await db.DeteriorationMatrixCells.AsNoTracking()
            .Where(c => c.Component == query.Component
                     && c.TypeGroup == query.TypeGroup
                     && c.MaterialGroup == query.MaterialGroup
                     && c.Region == query.Region)
            .ToListAsync(cancellationToken);

        var rowByRating = rows.ToDictionary(r => (int)r.FromRating);
        var pairsByMove = cells.ToDictionary(c => ((int)c.FromRating, (int)c.ToRating), c => c.Pairs);

        var dto = new List<DeteriorationRowDto>(10);
        for (var from = 0; from <= 9; from++)
        {
            rowByRating.TryGetValue(from, out var row);
            var rowTotal = row?.RowTotal ?? 0;
            // A row with no pairs is emitted as an empty row rather than omitted: a missing row and a
            // blank cell are indistinguishable from a rendering bug, and §4 commits to always showing
            // counts and row totals.
            var sufficient = rowTotal >= floor;

            var rowCells = new List<DeteriorationCellDto>(10);
            for (var to = 0; to <= 9; to++)
            {
                var cellPairs = pairsByMove.GetValueOrDefault((from, to), 0);
                rowCells.Add(new DeteriorationCellDto(
                    to,
                    cellPairs,
                    sufficient ? Math.Round(100d * cellPairs / rowTotal, 1) : null));
            }

            dto.Add(new DeteriorationRowDto(
                from,
                rowTotal,
                sufficient,
                row?.FirstFromYear,
                row?.LastFromYear,
                row?.YearPairsObserved ?? 0,
                rowCells));
        }

        var pairs = rows.Sum(r => r.RowTotal);

        // The unchanged share is a rate, so the floor applies to it too.
        //
        // It is measured over the rows that clear the floor and nothing else. Computing it across the
        // whole matrix would publish a percentage for evidence the very same response just suppressed:
        // in the 2026-07-26 build 395 of 1,028 cohort matrices have every row below the floor, 199 of
        // those would emit exactly "100.0%", and 56 rest on a single pair — and where a matrix has one
        // populated row, that percentage is byte for byte the P[i][i] the response returned as null.
        // A page cannot say "insufficient data" ten times and then state a rate (FR-1.3 AC-3).
        var sufficientRows = rows.Where(r => r.RowTotal >= floor).ToList();
        var ratedPairs = sufficientRows.Sum(r => r.RowTotal);
        var sufficientRatings = sufficientRows.Select(r => r.FromRating).ToHashSet();
        var unchanged = cells
            .Where(c => c.FromRating == c.ToRating && sufficientRatings.Contains(c.FromRating))
            .Sum(c => c.Pairs);

        var unchangedShare = ratedPairs == 0 ? (double?)null : Math.Round(100d * unchanged / ratedPairs, 1);

        // With no above-floor row there is no share to state, so the caveat ships without one rather
        // than with a word ("None") standing where a percentage belongs — which read as "every
        // transition changed" for a matrix that may have observed nothing at all.
        var caption = unchangedShare is null
            ? CadenceCaptionWithoutShare
            : string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                CadenceCaptionTemplate,
                $"{unchangedShare:0.0}%");

        var cohortComponents = new List<CohortComponentDto>
        {
            new(query.Component.ToString(), pairs, rows.Count, rows.Count(r => r.RowTotal >= floor)),
        };

        return TypedResults.Ok(new DeteriorationMatrixDto(
            query.Component.ToString(),
            new DeteriorationCohortDto(
                query.TypeGroup, query.MaterialGroup, query.Region,
                NbiCohorts.IsAllCohorts(query.TypeGroup), cohortComponents),
            dto,
            pairs,
            floor,
            dto.Count(r => !r.Sufficient),
            unchangedShare,
            MethodNote(run),
            caption,
            run?.MethodologyVersion ?? "unpublished",
            MethodologyUrl,
            Provenance(run)));
    }

    /// <summary>
    /// The run whose rows are actually on the table — not merely the newest completed run.
    /// <para>
    /// A load converges by deleting rows left pointing at an older run, so a run can be Completed and
    /// yet be serving nothing (re-publishing an earlier job is exactly that case, and so is a
    /// rollback). Taking the newest id instead would attach the wrong job id, catalog SHA and — worse
    /// — the wrong sample-size floor to matrices it never produced. The same reason FR-1.2 reads
    /// provenance off the rows it returned.
    /// </para>
    /// </summary>
    private static async Task<DeteriorationRun?> ServingRunAsync(
        SpanSightDbContext db, long? preferredRunId, CancellationToken cancellationToken)
    {
        // Prefer the run stamped on the rows just fetched, as FR-1.2 does. The sample-size floor and
        // the methodology version live on the run precisely so they can change, so reading them from
        // a different run than the data would suppress rates that were published as sufficient, or
        // attribute matrices to a document version they were never computed under.
        var runId = preferredRunId ?? await db.DeteriorationMatrixRows.AsNoTracking()
            .OrderByDescending(r => r.DeteriorationRunId)
            .Select(r => (long?)r.DeteriorationRunId)
            .FirstOrDefaultAsync(cancellationToken);

        return runId is null
            ? null
            : await db.DeteriorationRuns.AsNoTracking()
                .FirstOrDefaultAsync(
                    r => r.Id == runId && r.Status == DeteriorationRunStatus.Completed, cancellationToken);
    }

    private static DeteriorationProvenanceDto? Provenance(DeteriorationRun? run) =>
        run is null
            ? null
            : new DeteriorationProvenanceDto(
                run.JobRunId, run.CatalogSha256, run.CompletedUtc,
                run.FirstYear, run.LastYear, run.YearPairs, run.ComponentPairs);

    /// <summary>The dimension vocabulary, built from the same lookups the offline job's SQL mirrors.</summary>
    private static readonly DeteriorationDimensionsDto Dimensions = new(
        [.. Enum.GetNames<ConditionComponent>()],
        [.. NbiCohorts.TypeCodesByGroup.Select(g => new CohortGroupDto(
            g.Key, [.. g.Value.Select(code => new CohortCodeDto(code, NbiCodes.DecodeDesign(code)))])),
          new CohortGroupDto(NbiCohorts.NotPublished, [])],
        [.. NbiCohorts.MaterialCodesByGroup.Select(g => new CohortGroupDto(
            g.Key, [.. g.Value.Select(code => new CohortCodeDto(code, NbiCodes.DecodeMaterial(code)))])),
          new CohortGroupDto(NbiCohorts.NotPublished, [])],
        [.. NbiCohorts.Regions]);

    /// <summary>A validated matrix request. Public so the validation rules can be unit-tested without a database.</summary>
    public readonly record struct MatrixQuery(
        ConditionComponent Component, string TypeGroup, string MaterialGroup, string Region);

    /// <summary>
    /// Validates and normalises a matrix request, returning field-keyed errors for ProblemDetails.
    /// <para>
    /// The three cohort dimensions travel together: all omitted means the national context matrix, all
    /// supplied means one cohort. A partial cohort is rejected rather than silently completed, because
    /// a row that is 'All' in one dimension and a real group in another is neither a cohort nor the
    /// national total (methodology §4).
    /// </para>
    /// </summary>
    public static bool TryBuildQuery(
        string? component,
        string? typeGroup,
        string? materialGroup,
        string? region,
        out MatrixQuery query,
        out Dictionary<string, string[]> errors)
    {
        errors = [];
        query = default;

        // The component must be a name. Enum.TryParse also accepts the underlying number, which gives
        // two problems: an undefined value like 99 parses and would return 200 with a fabricated
        // family label over an empty grid — indistinguishable from a real family that published
        // nothing — and a defined one like 2 becomes an undocumented second spelling of
        // "Superstructure", making the OpenAPI contract untrue. Digits are rejected outright.
        var trimmedComponent = component?.Trim();
        var parsedComponent = trimmedComponent is { Length: > 0 }
            && !trimmedComponent.Any(ch => char.IsAsciiDigit(ch) || ch is '-' or '+')
            && Enum.TryParse<ConditionComponent>(trimmedComponent, ignoreCase: true, out var c)
            && Enum.IsDefined(c)
                ? c
                : (ConditionComponent?)null;

        if (parsedComponent is null)
        {
            errors["component"] = [string.IsNullOrWhiteSpace(component)
                ? $"component is required — one of {string.Join(", ", Enum.GetNames<ConditionComponent>())}."
                : $"Unknown component '{component}' — expected one of {string.Join(", ", Enum.GetNames<ConditionComponent>())}."];
        }

        var supplied = new[] { typeGroup, materialGroup, region }.Count(v => !string.IsNullOrWhiteSpace(v));
        if (supplied is not (0 or 3))
        {
            errors["typeGroup"] =
            [
                "typeGroup, materialGroup and region must all be supplied together (one cohort) or all " +
                "omitted (the national all-cohorts matrix).",
            ];
        }
        else if (supplied == 3)
        {
            Validate("typeGroup", typeGroup!, NbiCohorts.TypeGroups, errors);
            Validate("materialGroup", materialGroup!, NbiCohorts.MaterialGroups, errors);
            Validate("region", region!, NbiCohorts.Regions, errors);
        }

        if (errors.Count > 0)
        {
            return false;
        }

        // Canonical cannot be null here: Validate above rejected anything it does not recognise, so
        // reaching this line with three supplied dimensions means all three matched a known group.
        query = supplied == 0
            ? new MatrixQuery(parsedComponent!.Value, NbiCohorts.AllCohorts, NbiCohorts.AllCohorts, NbiCohorts.AllCohorts)
            : new MatrixQuery(
                parsedComponent!.Value,
                Canonical(typeGroup!, NbiCohorts.TypeGroups)!,
                Canonical(materialGroup!, NbiCohorts.MaterialGroups)!,
                Canonical(region!, NbiCohorts.Regions)!);
        return true;
    }

    private static void Validate(string field, string value, IReadOnlyList<string> allowed, Dictionary<string, string[]> errors)
    {
        // 'All' is reserved for the national matrix and is reached by omitting all three dimensions, so
        // asking for it by name is a client error rather than a second way in.
        if (Canonical(value, allowed) is null)
        {
            errors[field] = [$"Unknown {field} '{value.Trim()}' — expected one of {string.Join(", ", allowed)}."];
        }
    }

    private static string? Canonical(string value, IReadOnlyList<string> allowed) =>
        allowed.FirstOrDefault(a => string.Equals(a, value.Trim(), StringComparison.OrdinalIgnoreCase));
}
