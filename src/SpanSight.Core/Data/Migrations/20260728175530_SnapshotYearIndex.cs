using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpanSight.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class SnapshotYearIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_bridge_snapshot_year",
                schema: "core",
                table: "bridge",
                column: "snapshot_year");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_bridge_snapshot_year",
                schema: "core",
                table: "bridge");
        }
    }
}
