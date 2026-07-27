using System;

using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SpanSight.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AnalyticsDeteriorationMatrices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "deterioration_run",
                schema: "analytics",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    job_run_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    catalog_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    methodology_version = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    sample_floor = table.Column<int>(type: "integer", nullable: false),
                    started_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    first_year = table.Column<short>(type: "smallint", nullable: false),
                    last_year = table.Column<short>(type: "smallint", nullable: false),
                    year_pairs = table.Column<int>(type: "integer", nullable: false),
                    structure_pairs = table.Column<long>(type: "bigint", nullable: false),
                    component_pairs = table.Column<long>(type: "bigint", nullable: false),
                    cohorts = table.Column<int>(type: "integer", nullable: false),
                    matrix_rows = table.Column<int>(type: "integer", nullable: false),
                    cells = table.Column<int>(type: "integer", nullable: false),
                    rows_below_floor = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    error = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deterioration_run", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "deterioration_matrix_cell",
                schema: "analytics",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    component = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    type_group = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    material_group = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    region = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    from_rating = table.Column<short>(type: "smallint", nullable: false),
                    to_rating = table.Column<short>(type: "smallint", nullable: false),
                    pairs = table.Column<int>(type: "integer", nullable: false),
                    deterioration_run_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deterioration_matrix_cell", x => x.id);
                    table.ForeignKey(
                        name: "FK_deterioration_matrix_cell_deterioration_run_deterioration_r~",
                        column: x => x.deterioration_run_id,
                        principalSchema: "analytics",
                        principalTable: "deterioration_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "deterioration_matrix_row",
                schema: "analytics",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    component = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    type_group = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    material_group = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    region = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    from_rating = table.Column<short>(type: "smallint", nullable: false),
                    row_total = table.Column<int>(type: "integer", nullable: false),
                    first_from_year = table.Column<short>(type: "smallint", nullable: false),
                    last_from_year = table.Column<short>(type: "smallint", nullable: false),
                    year_pairs_observed = table.Column<short>(type: "smallint", nullable: false),
                    deterioration_run_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deterioration_matrix_row", x => x.id);
                    table.ForeignKey(
                        name: "FK_deterioration_matrix_row_deterioration_run_deterioration_ru~",
                        column: x => x.deterioration_run_id,
                        principalSchema: "analytics",
                        principalTable: "deterioration_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_deterioration_matrix_cell_component_type_group_material_gro~",
                schema: "analytics",
                table: "deterioration_matrix_cell",
                columns: new[] { "component", "type_group", "material_group", "region", "from_rating", "to_rating" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_deterioration_matrix_cell_deterioration_run_id",
                schema: "analytics",
                table: "deterioration_matrix_cell",
                column: "deterioration_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_deterioration_matrix_row_component_type_group_material_grou~",
                schema: "analytics",
                table: "deterioration_matrix_row",
                columns: new[] { "component", "type_group", "material_group", "region", "from_rating" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_deterioration_matrix_row_deterioration_run_id",
                schema: "analytics",
                table: "deterioration_matrix_row",
                column: "deterioration_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_deterioration_run_job_run_id",
                schema: "analytics",
                table: "deterioration_run",
                column: "job_run_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "deterioration_matrix_cell",
                schema: "analytics");

            migrationBuilder.DropTable(
                name: "deterioration_matrix_row",
                schema: "analytics");

            migrationBuilder.DropTable(
                name: "deterioration_run",
                schema: "analytics");
        }
    }
}
