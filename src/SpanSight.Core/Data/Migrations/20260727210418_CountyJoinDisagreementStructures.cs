using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpanSight.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class CountyJoinDisagreementStructures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "disagreement_structures",
                schema: "analytics",
                table: "county_join_run",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "structures",
                schema: "analytics",
                table: "county_join_disagreement",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "disagreement_structures",
                schema: "analytics",
                table: "county_join_run");

            migrationBuilder.DropColumn(
                name: "structures",
                schema: "analytics",
                table: "county_join_disagreement");
        }
    }
}
