using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LmuTracker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FollowsAndIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Track",
                table: "ScanEvents",
                type: "nvarchar(96)",
                maxLength: 96,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CarType",
                table: "RaceResults",
                type: "nvarchar(96)",
                maxLength: 96,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "Follows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DriverId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Follows", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScanEvents_EventStartsAt",
                table: "ScanEvents",
                column: "EventStartsAt");

            migrationBuilder.CreateIndex(
                name: "IX_ScanEvents_Track",
                table: "ScanEvents",
                column: "Track");

            migrationBuilder.CreateIndex(
                name: "IX_RaceResults_CarType",
                table: "RaceResults",
                column: "CarType");

            migrationBuilder.CreateIndex(
                name: "IX_Follows_UserId_DriverId",
                table: "Follows",
                columns: new[] { "UserId", "DriverId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Follows");

            migrationBuilder.DropIndex(
                name: "IX_ScanEvents_EventStartsAt",
                table: "ScanEvents");

            migrationBuilder.DropIndex(
                name: "IX_ScanEvents_Track",
                table: "ScanEvents");

            migrationBuilder.DropIndex(
                name: "IX_RaceResults_CarType",
                table: "RaceResults");

            migrationBuilder.AlterColumn<string>(
                name: "Track",
                table: "ScanEvents",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(96)",
                oldMaxLength: 96,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CarType",
                table: "RaceResults",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(96)",
                oldMaxLength: 96,
                oldNullable: true);
        }
    }
}
