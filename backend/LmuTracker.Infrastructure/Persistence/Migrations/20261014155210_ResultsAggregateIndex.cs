using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LmuTracker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ResultsAggregateIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // nvarchar(max) -> tamanho fixo: corta antes o que (improvavelmente) passar, para o ALTER não falhar.
            migrationBuilder.Sql(@"
UPDATE RaceResults SET Nationality = LEFT(Nationality, 16) WHERE LEN(Nationality) > 16;
UPDATE RaceResults SET CarClass = LEFT(CarClass, 32) WHERE LEN(CarClass) > 32;
UPDATE RaceResults SET FinishStatus = LEFT(FinishStatus, 32) WHERE LEN(FinishStatus) > 32;
UPDATE RaceResults SET DriverRankTitle = LEFT(DriverRankTitle, 32) WHERE LEN(DriverRankTitle) > 32;
UPDATE RaceResults SET SafetyRankTitle = LEFT(SafetyRankTitle, 32) WHERE LEN(SafetyRankTitle) > 32;");

            migrationBuilder.DropIndex(
                name: "IX_RaceResults_RaceSplitId",
                table: "RaceResults");

            migrationBuilder.AlterColumn<string>(
                name: "SafetyRankTitle",
                table: "RaceResults",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Nationality",
                table: "RaceResults",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "FinishStatus",
                table: "RaceResults",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "DriverRankTitle",
                table: "RaceResults",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CarClass",
                table: "RaceResults",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RaceResults_RaceSplitId",
                table: "RaceResults",
                column: "RaceSplitId")
                .Annotation("SqlServer:Include", new[] { "SteamId", "Name", "Nationality", "CarClass", "CarType", "ClassPosition", "ClassGridPos", "QualiClassPosition", "Laps", "BestLapTime", "FinishTime", "FinishStatus", "DriverRankAdjustment", "SafetyRankAdjustment", "Incidents", "Penalties", "DriverRankTitle", "DriverRankTier", "DriverRankProgress" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RaceResults_RaceSplitId",
                table: "RaceResults");

            migrationBuilder.AlterColumn<string>(
                name: "SafetyRankTitle",
                table: "RaceResults",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Nationality",
                table: "RaceResults",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(16)",
                oldMaxLength: 16,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "FinishStatus",
                table: "RaceResults",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "DriverRankTitle",
                table: "RaceResults",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CarClass",
                table: "RaceResults",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RaceResults_RaceSplitId",
                table: "RaceResults",
                column: "RaceSplitId");
        }
    }
}
