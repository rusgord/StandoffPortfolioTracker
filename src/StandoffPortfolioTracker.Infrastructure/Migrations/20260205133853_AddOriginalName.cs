using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StandoffPortfolioTracker.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOriginalName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OriginalName",
                table: "ItemBases",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OriginalName",
                table: "ItemBases");
        }
    }
}
