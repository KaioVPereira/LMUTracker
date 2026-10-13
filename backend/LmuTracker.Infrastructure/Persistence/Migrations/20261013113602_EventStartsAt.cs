using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LmuTracker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EventStartsAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EventStartsAt",
                table: "ScanEvents",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EventStartsAt",
                table: "ScanEvents");
        }
    }
}
