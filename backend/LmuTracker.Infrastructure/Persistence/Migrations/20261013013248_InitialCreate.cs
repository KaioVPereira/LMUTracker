using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LmuTracker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Drivers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SteamId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RaceOsPlayerId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: false),
                    Nationality = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Badge = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DriverRankTitle = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DriverRankTier = table.Column<int>(type: "int", nullable: true),
                    SafetyRankTitle = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SafetyRankTier = table.Column<int>(type: "int", nullable: true),
                    LastSeenAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Drivers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScanEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SeriesId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EventType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Tier = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Track = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Scene = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RaceStartsAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RaceEndsAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RaceOsStatus = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TotalSplits = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    FetchedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FetchAttempts = table.Column<int>(type: "int", nullable: false),
                    FirstSeenAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Username = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SteamId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    RaceOsPlayerId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LinkedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RaceSplits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScanEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SplitNo = table.Column<int>(type: "int", nullable: false),
                    MostLapsCompleted = table.Column<int>(type: "int", nullable: false),
                    DriverCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RaceSplits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RaceSplits_ScanEvents_ScanEventId",
                        column: x => x.ScanEventId,
                        principalTable: "ScanEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RaceResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RaceSplitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SteamId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    RaceOsPlayerId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: false),
                    Nationality = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Badge = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CarClass = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CarNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CarType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    VehName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TeamName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GridPos = table.Column<int>(type: "int", nullable: true),
                    Position = table.Column<int>(type: "int", nullable: true),
                    ClassGridPos = table.Column<int>(type: "int", nullable: true),
                    ClassPosition = table.Column<int>(type: "int", nullable: true),
                    Laps = table.Column<int>(type: "int", nullable: false),
                    BestLapTime = table.Column<double>(type: "float", nullable: true),
                    FinishTime = table.Column<double>(type: "float", nullable: true),
                    FinishStatus = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Points = table.Column<int>(type: "int", nullable: false),
                    DriverRankAdjustment = table.Column<int>(type: "int", nullable: true),
                    SafetyRankAdjustment = table.Column<int>(type: "int", nullable: true),
                    JokerPlayed = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RaceResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RaceResults_RaceSplits_RaceSplitId",
                        column: x => x.RaceSplitId,
                        principalTable: "RaceSplits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Drivers_SteamId",
                table: "Drivers",
                column: "SteamId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RaceResults_RaceSplitId",
                table: "RaceResults",
                column: "RaceSplitId");

            migrationBuilder.CreateIndex(
                name: "IX_RaceResults_SteamId",
                table: "RaceResults",
                column: "SteamId");

            migrationBuilder.CreateIndex(
                name: "IX_RaceSplits_ScanEventId_SplitNo",
                table: "RaceSplits",
                columns: new[] { "ScanEventId", "SplitNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScanEvents_EventId",
                table: "ScanEvents",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScanEvents_RaceEndsAt",
                table: "ScanEvents",
                column: "RaceEndsAt");

            migrationBuilder.CreateIndex(
                name: "IX_ScanEvents_Status",
                table: "ScanEvents",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_SteamId",
                table: "Users",
                column: "SteamId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Drivers");

            migrationBuilder.DropTable(
                name: "RaceResults");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "RaceSplits");

            migrationBuilder.DropTable(
                name: "ScanEvents");
        }
    }
}
