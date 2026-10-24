using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LmuTracker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LiveSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LiveCheckedAt",
                table: "ScanEvents",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LiveData",
                table: "ScanEvents",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "LiveDetail",
                table: "ScanEvents",
                type: "varbinary(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LiveEntriesAt",
                table: "ScanEvents",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Drivers_Name",
                table: "Drivers",
                column: "Name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Drivers_Name",
                table: "Drivers");

            migrationBuilder.DropColumn(
                name: "LiveCheckedAt",
                table: "ScanEvents");

            migrationBuilder.DropColumn(
                name: "LiveData",
                table: "ScanEvents");

            migrationBuilder.DropColumn(
                name: "LiveDetail",
                table: "ScanEvents");

            migrationBuilder.DropColumn(
                name: "LiveEntriesAt",
                table: "ScanEvents");
        }
    }
}
