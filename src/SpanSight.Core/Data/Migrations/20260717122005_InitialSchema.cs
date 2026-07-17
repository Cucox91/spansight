using System;
using System.Collections.Generic;

using Microsoft.EntityFrameworkCore.Migrations;

using NetTopologySuite.Geometries;

using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SpanSight.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "core");

            migrationBuilder.EnsureSchema(
                name: "ops");

            migrationBuilder.EnsureSchema(
                name: "quarantine");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:postgis", ",,");

            migrationBuilder.CreateTable(
                name: "bridge",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    state_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    structure_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    record_type = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    county_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    features_intersected = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    facility_carried = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    location_text = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    location = table.Column<Point>(type: "geometry(Point, 4326)", nullable: false),
                    year_built = table.Column<int>(type: "integer", nullable: true),
                    adt = table.Column<int>(type: "integer", nullable: true),
                    material_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    design_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    structure_length_meters = table.Column<decimal>(type: "numeric(9,1)", precision: 9, scale: 1, nullable: true),
                    deck_condition = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: true),
                    superstructure_condition = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: true),
                    substructure_condition = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: true),
                    culvert_condition = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: true),
                    lowest_rating = table.Column<int>(type: "integer", nullable: true),
                    condition_class = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    source_format = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    snapshot_year = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bridge", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ingestion_run",
                schema: "ops",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    started_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    source_file = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    source_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    snapshot_year = table.Column<int>(type: "integer", nullable: false),
                    rows_read = table.Column<int>(type: "integer", nullable: false),
                    rows_loaded = table.Column<int>(type: "integer", nullable: false),
                    rows_quarantined = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    error = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ingestion_run", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "quarantine_row",
                schema: "quarantine",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ingestion_run_id = table.Column<long>(type: "bigint", nullable: false),
                    line_number = table.Column<int>(type: "integer", nullable: false),
                    state_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    structure_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    reasons = table.Column<List<string>>(type: "text[]", nullable: false),
                    raw_line = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quarantine_row", x => x.id);
                    table.ForeignKey(
                        name: "FK_quarantine_row_ingestion_run_ingestion_run_id",
                        column: x => x.ingestion_run_id,
                        principalSchema: "ops",
                        principalTable: "ingestion_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_bridge_adt",
                schema: "core",
                table: "bridge",
                column: "adt");

            migrationBuilder.CreateIndex(
                name: "IX_bridge_condition_class",
                schema: "core",
                table: "bridge",
                column: "condition_class");

            migrationBuilder.CreateIndex(
                name: "IX_bridge_design_code",
                schema: "core",
                table: "bridge",
                column: "design_code");

            migrationBuilder.CreateIndex(
                name: "IX_bridge_location",
                schema: "core",
                table: "bridge",
                column: "location")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "IX_bridge_material_code",
                schema: "core",
                table: "bridge",
                column: "material_code");

            migrationBuilder.CreateIndex(
                name: "IX_bridge_state_code_county_code",
                schema: "core",
                table: "bridge",
                columns: new[] { "state_code", "county_code" });

            migrationBuilder.CreateIndex(
                name: "IX_bridge_state_code_structure_number_record_type",
                schema: "core",
                table: "bridge",
                columns: new[] { "state_code", "structure_number", "record_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bridge_year_built",
                schema: "core",
                table: "bridge",
                column: "year_built");

            migrationBuilder.CreateIndex(
                name: "IX_ingestion_run_source_sha256_snapshot_year",
                schema: "ops",
                table: "ingestion_run",
                columns: new[] { "source_sha256", "snapshot_year" });

            migrationBuilder.CreateIndex(
                name: "IX_quarantine_row_ingestion_run_id",
                schema: "quarantine",
                table: "quarantine_row",
                column: "ingestion_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_quarantine_row_state_code",
                schema: "quarantine",
                table: "quarantine_row",
                column: "state_code");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bridge",
                schema: "core");

            migrationBuilder.DropTable(
                name: "quarantine_row",
                schema: "quarantine");

            migrationBuilder.DropTable(
                name: "ingestion_run",
                schema: "ops");
        }
    }
}
