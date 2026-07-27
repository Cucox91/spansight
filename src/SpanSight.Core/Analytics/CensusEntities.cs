namespace SpanSight.Core.Analytics;

public enum CountyJoinRunStatus
{
    Running = 1,

    Completed = 2,

    Failed = 3,
}

/// <summary>
/// One execution of <c>tools/census/join-counties.sh</c>, as recorded when its results were loaded
/// (FR-1.5 AC-2, NFR-3).
/// <para>
/// This is the coverage metric itself, not merely bookkeeping: AC-2 asks for the share of bridges
/// matched to a county polygon to be measured and published, so the numerator, the denominator and
/// the containment rule they were measured under all live on the run and travel together to the QA
/// page. A percentage computed anywhere else could disagree with the rows it describes.
/// </para>
/// <para>
/// Separate from <see cref="TrendRun"/> and <see cref="DeteriorationRun"/> on the same reasoning
/// those two are separate from each other: the three jobs publish and roll back independently
/// (RUNBOOK §10.3/§10.4/§10.5), so a bad join publish cannot take the trend series down with it.
/// </para>
/// </summary>
public class CountyJoinRun
{
    public long Id { get; set; }

    /// <summary>The job's own run id from <c>manifest.json</c> (e.g. <c>county-join-20260727T202154Z</c>).</summary>
    public required string JobRunId { get; set; }

    /// <summary>SHA-256 of <c>tools/census/catalog.json</c> — pins which TIGER/ACS vintages were read.</summary>
    public required string CatalogSha256 { get; set; }

    /// <summary>Version of the method in <c>tools/census/county-join.sql</c> these figures were computed under.</summary>
    public required string MethodVersion { get; set; }

    /// <summary>
    /// The containment predicate the job used, carried rather than assumed. <c>ST_Within</c> excludes
    /// the boundary, which is why a structure sitting exactly on a county line appears as a miss
    /// rather than as an arbitrary assignment — a reader of the coverage figure has to be able to see
    /// that rule without reading the SQL.
    /// </summary>
    public required string ContainmentPredicate { get; set; }

    public DateTimeOffset StartedUtc { get; set; }

    public DateTimeOffset? CompletedUtc { get; set; }

    /// <summary>Every row of <c>core.bridge</c> the join was run over — the denominator a reader is served.</summary>
    public long Bridges { get; set; }

    public long Matched { get; set; }

    public long Unmatched { get; set; }

    /// <summary>
    /// Rows with NBI record type 1 — the structure itself. Published beside <see cref="Bridges"/>
    /// because the two are genuinely different populations: the serving table also holds the route
    /// records published *under* a structure, and "bridge" everywhere else in the product (FR-1.2,
    /// FR-1.3) means record type 1.
    /// </summary>
    public long Structures { get; set; }

    public long StructuresMatched { get; set; }

    /// <summary>Matched structures whose containing polygon is the county item 3 published.</summary>
    public long Agree { get; set; }

    public long DifferentCountySameState { get; set; }

    public long DifferentState { get; set; }

    /// <summary>Matched structures for which item 3 carried no usable code, so there was nothing to compare.</summary>
    public long CountyNotPublished { get; set; }

    public int Counties { get; set; }

    /// <summary>Counties with a TIGER boundary and no ACS population row — an absence, never a zero.</summary>
    public int CountiesWithoutPopulation { get; set; }

    public int Misses { get; set; }

    public int Disagreements { get; set; }

    public long DisagreementBridges { get; set; }

    public CountyJoinRunStatus Status { get; set; }

    public string? Error { get; set; }
}

/// <summary>
/// One county or county-equivalent as the Census publishes it: TIGER geography plus the ACS
/// population estimate joined on the 5-digit GEOID (FR-1.5 AC-1/AC-3).
/// <para>
/// Named for its source. NBI item 3 also publishes a county code, and the two are not the same
/// thing — Connecticut is the standing proof, where item 3 still carries the eight legacy counties
/// that TIGER replaced with nine planning regions. Calling this <c>census_county</c> keeps which
/// publisher a name and a population came from visible at every call site.
/// </para>
/// <para>
/// No geometry is stored. The polygons are 99 MB and their only consumer is the offline join; the
/// serving database holds the compact result, which is ADR-005's rule.
/// </para>
/// </summary>
public class CensusCounty
{
    public long Id { get; set; }

    /// <summary>5-digit state+county FIPS, as text — <c>06037</c> is not the number 6037.</summary>
    public required string CountyFips { get; set; }

    public required string StateFips { get; set; }

    /// <summary>TIGER <c>NAME</c>, e.g. "Miami-Dade".</summary>
    public required string Name { get; set; }

    /// <summary>TIGER <c>NAMELSAD</c> — the name with its legal/statistical description, e.g. "Miami-Dade County".</summary>
    public required string NameLsad { get; set; }

    public long LandAreaM2 { get; set; }

    public long WaterAreaM2 { get; set; }

    public short TigerVintage { get; set; }

    /// <summary>
    /// ACS 5-year estimate of total population (table B01003). Null where the Census publishes a
    /// boundary but no estimate — the four island areas — and null is the published fact there, so it
    /// must reach the UI as a stated absence rather than as a zero (GR-6).
    /// </summary>
    public long? Population { get; set; }

    /// <summary>
    /// Published margin of error for <see cref="Population"/>. Very often null: the Census suppresses
    /// it with a jam value for most counties, and the converter records how many. An estimate shown
    /// without its margin must still be labelled an estimate.
    /// </summary>
    public long? PopulationMoe { get; set; }

    /// <summary>ACS release year, cited wherever the population is displayed (FR-1.5 AC-3).</summary>
    public short? AcsVintage { get; set; }

    /// <summary>The five years the estimate pools, e.g. "2020-2024".</summary>
    public string? AcsPeriod { get; set; }

    /// <summary>ACS table id, e.g. "B01003".</summary>
    public string? AcsTable { get; set; }

    public long CountyJoinRunId { get; set; }
}

/// <summary>
/// One structure whose coordinate fell inside no county polygon, with the reason and the evidence to
/// act on it (FR-1.5 AC-2: "misses are quarantined with reasons").
/// <para>
/// Published per structure rather than aggregated because there are 55 of them nationally and the
/// whole point of a quarantine is that someone can look at the row. This is the only place in the
/// product where the join names an individual structure, and it names it as a data-quality exception,
/// never as a geography that competes with the published county code.
/// </para>
/// </summary>
public class CountyJoinMiss
{
    public long Id { get; set; }

    public required string StateCode { get; set; }

    public required string StructureNumber { get; set; }

    /// <summary>Item 5A. Part of the natural key, exactly as in <c>core.bridge</c>.</summary>
    public required string RecordType { get; set; }

    /// <summary>The county code item 3 published for this structure, if any.</summary>
    public string? NbiCountyFips { get; set; }

    /// <summary>
    /// <c>on_county_boundary</c> — the point touches at least one polygon but is inside none, i.e. it
    /// lies exactly on a boundary line; or <c>outside_all_county_polygons</c> — no polygon touches it.
    /// </summary>
    public required string Reason { get; set; }

    /// <summary>How many county polygons the point touches. Zero for an outside miss, at least one for a boundary miss.</summary>
    public int TouchingCounties { get; set; }

    /// <summary>
    /// The closest county, or null when none lies within the job's bounded search radius. Null here
    /// means "not looked far enough to say", which is a different fact from "no county nearby".
    /// </summary>
    public string? NearestCountyFips { get; set; }

    /// <summary>
    /// Metres to the nearest county polygon; zero for a boundary miss. This is what separates a metre
    /// of shoreline slop from a coordinate in the wrong ocean, and both are "unmatched" without it.
    /// </summary>
    public long? NearestDistanceMeters { get; set; }

    public double Longitude { get; set; }

    public double Latitude { get; set; }

    public long CountyJoinRunId { get; set; }
}

/// <summary>
/// One (published county code → containing polygon) pair that disagrees, with how many structures
/// take that path (FR-1.5 AC-2 — the data-quality story).
/// <para>
/// Aggregated to the pair, not published per structure. The pair is what a reader can act on, it is
/// three orders of magnitude smaller than the structures behind it, and — the reason that matters —
/// publishing a per-structure county of SpanSight's own devising next to the published one would
/// invite it to be read as a correction. It is not one: item 3 remains the county this product
/// reports everywhere (GR-6).
/// </para>
/// </summary>
public class CountyJoinDisagreement
{
    public long Id { get; set; }

    /// <summary>The county code item 3 published. Null for the <c>county_not_published</c> kind.</summary>
    public string? NbiCountyFips { get; set; }

    /// <summary>The county whose polygon actually contains the coordinate.</summary>
    public required string CountyFips { get; set; }

    /// <summary><c>different_county_same_state</c>, <c>different_state</c> or <c>county_not_published</c>.</summary>
    public required string Kind { get; set; }

    public int Bridges { get; set; }

    /// <summary>
    /// Whether the boundary file still carries the published code. False is the single fact that
    /// explains most disagreement: Connecticut's eight legacy county codes, which item 3 publishes in
    /// every vintage through 2025 and which TIGER replaced with nine planning regions — 5,644
    /// structures on the 2025 load, every one of them "disagreeing" because the code names a county
    /// that no longer exists rather than because the coordinate is wrong. Null when no code was
    /// published, since the question does not apply.
    /// </summary>
    public bool? NbiFipsInTiger { get; set; }

    public long CountyJoinRunId { get; set; }
}
