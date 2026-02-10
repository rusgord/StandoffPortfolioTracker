using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StandoffPortfolioTracker.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UserFieldUpdate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FavoriteSkinsJson",
                table: "AspNetUsers",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "IsBlogEnabled",
                table: "AspNetUsers",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsGameIdPublic",
                table: "AspNetUsers",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ProfileFrame",
                table: "AspNetUsers",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "StandoffGameId",
                table: "AspNetUsers",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FavoriteSkinsJson",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "IsBlogEnabled",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "IsGameIdPublic",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "ProfileFrame",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "StandoffGameId",
                table: "AspNetUsers");
        }
    }
}
