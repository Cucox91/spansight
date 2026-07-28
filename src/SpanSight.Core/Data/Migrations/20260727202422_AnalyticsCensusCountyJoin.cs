using System;

using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SpanSight.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AnalyticsCensusCountyJoin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "county_join_run",
                schema: "analytics",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    job_run_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    catalog_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    method_version = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    containment_predicate = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    started_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    bridges = table.Column<long>(type: "bigint", nullable: false),
                    matched = table.Column<long>(type: "bigint", nullable: false),
                    unmatched = table.Column<long>(type: "bigint", nullable: false),
                    structures = table.Column<long>(type: "bigint", nullable: false),
                    structures_matched = table.Column<long>(type: "bigint", nullable: false),
                    agree = table.Column<long>(type: "bigint", nullable: false),
                    different_county_same_state = table.Column<long>(type: "bigint", nullable: false),
                    different_state = table.Column<long>(type: "bigint", nullable: false),
                    county_not_published = table.Column<long>(type: "bigint", nullable: false),
                    counties = table.Column<int>(type: "integer", nullable: false),
                    counties_without_population = table.Column<int>(type: "integer", nullable: false),
                    misses = table.Column<int>(type: "integer", nullable: false),
                    disagreements = table.Column<int>(type: "integer", nullable: false),
                    disagreement_bridges = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    error = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_county_join_run", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "census_county",
                schema: "analytics",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    county_fips = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    state_fips = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name_lsad = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    land_area_m2 = table.Column<long>(type: "bigint", nullable: false),
                    water_area_m2 = table.Column<long>(type: "bigint", nullable: false),
                    tiger_vintage = table.Column<short>(type: "smallint", nullable: false),
                    population = table.Column<long>(type: "bigint", nullable: true),
                    population_moe = table.Column<long>(type: "bigint", nullable: true),
                    acs_vintage = table.Column<short>(type: "smallint", nullable: true),
                    acs_period = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    acs_table = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    county_join_run_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_census_county", x => x.id);
                    table.ForeignKey(
                        name: "FK_census_county_county_join_run_county_join_run_id",
                        column: x => x.county_join_run_id,
                        principalSchema: "analytics",
                        principalTable: "county_join_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "county_join_disagreement",
                schema: "analytics",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    nbi_county_fips = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    county_fips = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    bridges = table.Column<int>(type: "integer", nullable: false),
                    nbi_fips_in_tiger = table.Column<bool>(type: "boolean", nullable: true),
                    county_join_run_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_county_join_disagreement", x => x.id);
                    table.ForeignKey(
                        name: "FK_county_join_disagreement_county_join_run_county_join_run_id",
                        column: x => x.county_join_run_id,
                        principalSchema: "analytics",
                        principalTable: "county_join_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "county_join_miss",
                schema: "analytics",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    state_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    structure_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    record_type = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    nbi_county_fips = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    touching_counties = table.Column<int>(type: "integer", nullable: false),
                    nearest_county_fips = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    nearest_distance_meters = table.Column<long>(type: "bigint", nullable: true),
                    longitude = table.Column<double>(type: "double precision", nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: false),
                    county_join_run_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_county_join_miss", x => x.id);
                    table.ForeignKey(
                        name: "FK_county_join_miss_county_join_run_county_join_run_id",
                        column: x => x.county_join_run_id,
                        principalSchema: "analytics",
                        principalTable: "county_join_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_census_county_county_fips",
                schema: "analytics",
                table: "census_county",
                column: "county_fips",
                unique: true)
                .Annotation("Npgsql:IndexInclude", new[] { "state_fips", "name", "name_lsad", "population", "population_moe", "acs_vintage", "acs_period", "tiger_vintage" });

            migrationBuilder.CreateIndex(
                name: "IX_census_county_county_join_run_id",
                schema: "analytics",
                table: "census_county",
                column: "county_join_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_census_county_state_fips",
                schema: "analytics",
                table: "census_county",
                column: "state_fips");

            migrationBuilder.CreateIndex(
                name: "IX_county_join_disagreement_county_join_run_id",
                schema: "analytics",
                table: "county_join_disagreement",
                column: "county_join_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_county_join_disagreement_nbi_county_fips_county_fips",
                schema: "analytics",
                table: "county_join_disagreement",
                columns: new[] { "nbi_county_fips", "county_fips" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_county_join_miss_county_join_run_id",
                schema: "analytics",
                table: "county_join_miss",
                column: "county_join_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_county_join_miss_state_code_structure_number_record_type",
                schema: "analytics",
                table: "county_join_miss",
                columns: new[] { "state_code", "structure_number", "record_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_county_join_run_job_run_id",
                schema: "analytics",
                table: "county_join_run",
                column: "job_run_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "census_county",
                schema: "analytics");

            migrationBuilder.DropTable(
                name: "county_join_disagreement",
                schema: "analytics");

            migrationBuilder.DropTable(
                name: "county_join_miss",
                schema: "analytics");

            migrationBuilder.DropTable(
                name: "county_join_run",
                schema: "analytics");
        }
    }
}
