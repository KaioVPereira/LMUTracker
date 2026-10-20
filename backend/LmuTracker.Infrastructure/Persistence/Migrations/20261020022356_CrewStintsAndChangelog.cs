using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LmuTracker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CrewStintsAndChangelog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastSeenChangelogAt",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CrewChecked",
                table: "ScanEvents",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "RaceCrewStints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RaceResultId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Seq = table.Column<int>(type: "int", nullable: false),
                    StartLap = table.Column<int>(type: "int", nullable: false),
                    EndLap = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: false),
                    SteamId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    BestLapTime = table.Column<double>(type: "float", nullable: true),
                    DriverRankAdjustment = table.Column<int>(type: "int", nullable: true),
                    SafetyRankAdjustment = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RaceCrewStints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RaceCrewStints_RaceResults_RaceResultId",
                        column: x => x.RaceResultId,
                        principalTable: "RaceResults",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RaceCrewStints_RaceResultId",
                table: "RaceCrewStints",
                column: "RaceResultId");

            migrationBuilder.CreateIndex(
                name: "IX_RaceCrewStints_SteamId",
                table: "RaceCrewStints",
                column: "SteamId")
                .Annotation("SqlServer:Include", new[] { "RaceResultId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RaceCrewStints");

            migrationBuilder.DropColumn(
                name: "LastSeenChangelogAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CrewChecked",
                table: "ScanEvents");
        }
    }
}
