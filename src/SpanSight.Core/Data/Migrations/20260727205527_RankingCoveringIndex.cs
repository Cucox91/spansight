using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpanSight.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class RankingCoveringIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_bridge_ranking",
                schema: "core",
                table: "bridge",
                column: "record_type")
                .Annotation("Npgsql:IndexInclude", new[] { "state_code", "county_code", "design_code", "material_code", "condition_class" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_bridge_ranking",
                schema: "core",
                table: "bridge");
        }
    }
}
