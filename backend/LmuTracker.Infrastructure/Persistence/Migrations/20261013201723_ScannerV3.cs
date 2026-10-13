using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LmuTracker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScannerV3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Classes",
                table: "ScanEvents",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "ConfigData",
                table: "ScanEvents",
                type: "varbinary(max)",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "ExtraData",
                table: "RaceSplits",
                type: "varbinary(max)",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "ExtraData",
                table: "RaceResults",
                type: "varbinary(max)",
                nullable: true);

            // Piloto fora do array drivers vinha com SteamId "" e virava um "piloto" só com centenas de corridas.
            migrationBuilder.Sql("UPDATE RaceResults SET SteamId = NULL WHERE SteamId = '';");

            // Classes das corridas já baixadas, na mesma ordem que o scanner usa.
            migrationBuilder.Sql(@"
UPDATE e SET Classes = c.Classes
FROM ScanEvents e
JOIN (
    SELECT x.ScanEventId, STRING_AGG(x.CarClass, ',') WITHIN GROUP (ORDER BY x.Ord, x.CarClass) AS Classes
    FROM (
        SELECT DISTINCT s.ScanEventId, r.CarClass,
            CASE r.CarClass WHEN 'Hyper' THEN 0 WHEN 'LMP2' THEN 1 WHEN 'LMP2_ELMS' THEN 2
                            WHEN 'LMP3' THEN 3 WHEN 'GT3' THEN 4 WHEN 'LMGT3' THEN 5 ELSE 99 END AS Ord
        FROM RaceResults r JOIN RaceSplits s ON s.Id = r.RaceSplitId
        WHERE r.CarClass IS NOT NULL AND r.CarClass <> ''
    ) x
    GROUP BY x.ScanEventId
) c ON c.ScanEventId = e.Id;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Classes",
                table: "ScanEvents");

            migrationBuilder.DropColumn(
                name: "ConfigData",
                table: "ScanEvents");

            migrationBuilder.DropColumn(
                name: "ExtraData",
                table: "RaceSplits");

            migrationBuilder.DropColumn(
                name: "ExtraData",
                table: "RaceResults");
        }
    }
}
