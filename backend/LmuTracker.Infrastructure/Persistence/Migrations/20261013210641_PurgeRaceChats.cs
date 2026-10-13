using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LmuTracker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PurgeRaceChats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A primeira versão do scanner v3 guardou o chat que o RaceOS manda aninhado em streams[0].chats
            // (o filtro só olhava o primeiro nível). Apaga o resultado cru dos splits e devolve essas corridas
            // à fila: o novo download grava tudo de novo, já sem chat em nenhum nível.
            migrationBuilder.Sql("UPDATE RaceSplits SET ExtraData = NULL WHERE ExtraData IS NOT NULL;");
            migrationBuilder.Sql("UPDATE ScanEvents SET DetailVersion = 2 WHERE DetailVersion = 3;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
