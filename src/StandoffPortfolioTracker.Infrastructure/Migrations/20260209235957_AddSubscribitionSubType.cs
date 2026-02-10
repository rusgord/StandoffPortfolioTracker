using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StandoffPortfolioTracker.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscribitionSubType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SubType",
                table: "AspNetUsers",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SubType",
                table: "AspNetUsers");
        }
    }
}
