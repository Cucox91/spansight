using System;

using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SpanSight.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AnalyticsConditionTrends : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "analytics");

            migrationBuilder.CreateTable(
                name: "trend_run",
                schema: "analytics",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    job_run_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    catalog_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    started_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    first_year = table.Column<short>(type: "smallint", nullable: false),
                    last_year = table.Column<short>(type: "smallint", nullable: false),
                    vintages = table.Column<int>(type: "integer", nullable: false),
                    bridges = table.Column<int>(type: "integer", nullable: false),
                    observations = table.Column<long>(type: "bigint", nullable: false),
                    rollup_rows = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    error = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trend_run", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bridge_condition_series",
                schema: "analytics",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    state_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    structure_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    first_year = table.Column<short>(type: "smallint", nullable: false),
                    last_year = table.Column<short>(type: "smallint", nullable: false),
                    observed_years = table.Column<short>(type: "smallint", nullable: false),
                    ratings = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    trend_run_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bridge_condition_series", x => x.id);
                    table.ForeignKey(
                        name: "FK_bridge_condition_series_trend_run_trend_run_id",
                        column: x => x.trend_run_id,
                        principalSchema: "analytics",
                        principalTable: "trend_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "condition_rollup",
                schema: "analytics",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    level = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    fips = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    vintage_year = table.Column<short>(type: "smallint", nullable: false),
                    total = table.Column<int>(type: "integer", nullable: false),
                    good = table.Column<int>(type: "integer", nullable: false),
                    fair = table.Column<int>(type: "integer", nullable: false),
                    poor = table.Column<int>(type: "integer", nullable: false),
                    unknown = table.Column<int>(type: "integer", nullable: false),
                    trend_run_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_condition_rollup", x => x.id);
                    table.ForeignKey(
                        name: "FK_condition_rollup_trend_run_trend_run_id",
                        column: x => x.trend_run_id,
                        principalSchema: "analytics",
                        principalTable: "trend_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_bridge_condition_series_state_code_structure_number",
                schema: "analytics",
                table: "bridge_condition_series",
                columns: new[] { "state_code", "structure_number" },
                unique: true)
                .Annotation("Npgsql:IndexInclude", new[] { "first_year", "last_year", "observed_years", "ratings" });

            migrationBuilder.CreateIndex(
                name: "IX_bridge_condition_series_trend_run_id",
                schema: "analytics",
                table: "bridge_condition_series",
                column: "trend_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_condition_rollup_level_fips_vintage_year",
                schema: "analytics",
                table: "condition_rollup",
                columns: new[] { "level", "fips", "vintage_year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_condition_rollup_trend_run_id",
                schema: "analytics",
                table: "condition_rollup",
                column: "trend_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_trend_run_job_run_id",
                schema: "analytics",
                table: "trend_run",
                column: "job_run_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bridge_condition_series",
                schema: "analytics");

            migrationBuilder.DropTable(
                name: "condition_rollup",
                schema: "analytics");

            migrationBuilder.DropTable(
                name: "trend_run",
                schema: "analytics");
        }
    }
}
