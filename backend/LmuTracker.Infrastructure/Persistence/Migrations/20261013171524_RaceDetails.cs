using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LmuTracker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RaceDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DetailVersion",
                table: "ScanEvents",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DriverRankProgress",
                table: "RaceResults",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DriverRankTier",
                table: "RaceResults",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DriverRankTitle",
                table: "RaceResults",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "LapsData",
                table: "RaceResults",
                type: "varbinary(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PitStops",
                table: "RaceResults",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "QualiBestLapTime",
                table: "RaceResults",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QualiClassPosition",
                table: "RaceResults",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QualiPosition",
                table: "RaceResults",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SafetyRankProgress",
                table: "RaceResults",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SafetyRankTier",
                table: "RaceResults",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SafetyRankTitle",
                table: "RaceResults",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DriverRankProgress",
                table: "Drivers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SafetyRankProgress",
                table: "Drivers",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DetailVersion",
                table: "ScanEvents");

            migrationBuilder.DropColumn(
                name: "DriverRankProgress",
                table: "RaceResults");

            migrationBuilder.DropColumn(
                name: "DriverRankTier",
                table: "RaceResults");

            migrationBuilder.DropColumn(
                name: "DriverRankTitle",
                table: "RaceResults");

            migrationBuilder.DropColumn(
                name: "LapsData",
                table: "RaceResults");

            migrationBuilder.DropColumn(
                name: "PitStops",
                table: "RaceResults");

            migrationBuilder.DropColumn(
                name: "QualiBestLapTime",
                table: "RaceResults");

            migrationBuilder.DropColumn(
                name: "QualiClassPosition",
                table: "RaceResults");

            migrationBuilder.DropColumn(
                name: "QualiPosition",
                table: "RaceResults");

            migrationBuilder.DropColumn(
                name: "SafetyRankProgress",
                table: "RaceResults");

            migrationBuilder.DropColumn(
                name: "SafetyRankTier",
                table: "RaceResults");

            migrationBuilder.DropColumn(
                name: "SafetyRankTitle",
                table: "RaceResults");

            migrationBuilder.DropColumn(
                name: "DriverRankProgress",
                table: "Drivers");

            migrationBuilder.DropColumn(
                name: "SafetyRankProgress",
                table: "Drivers");
        }
    }
}
