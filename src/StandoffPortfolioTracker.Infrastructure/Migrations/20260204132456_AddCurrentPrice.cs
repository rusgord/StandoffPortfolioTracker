using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StandoffPortfolioTracker.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCurrentPrice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CurrentMarketPrice",
                table: "ItemBases",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentMarketPrice",
                table: "ItemBases");
        }
    }
}
