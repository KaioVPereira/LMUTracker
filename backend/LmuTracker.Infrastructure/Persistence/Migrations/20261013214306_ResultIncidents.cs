using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LmuTracker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ResultIncidents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Incidents",
                table: "RaceResults",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Penalties",
                table: "RaceResults",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Incidents",
                table: "RaceResults");

            migrationBuilder.DropColumn(
                name: "Penalties",
                table: "RaceResults");
        }
    }
}
