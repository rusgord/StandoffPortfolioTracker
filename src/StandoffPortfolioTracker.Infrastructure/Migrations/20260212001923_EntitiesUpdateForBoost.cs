using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StandoffPortfolioTracker.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EntitiesUpdateForBoost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "PortfolioAccounts",
                type: "varchar(255)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "longtext")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastUpdate",
                table: "ItemBases",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioAccounts_UserId",
                table: "PortfolioAccounts",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_PortfolioAccounts_AspNetUsers_UserId",
                table: "PortfolioAccounts",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PortfolioAccounts_AspNetUsers_UserId",
                table: "PortfolioAccounts");

            migrationBuilder.DropIndex(
                name: "IX_PortfolioAccounts_UserId",
                table: "PortfolioAccounts");

            migrationBuilder.DropColumn(
                name: "LastUpdate",
                table: "ItemBases");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "PortfolioAccounts",
                type: "longtext",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(255)")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
        }
    }
}
