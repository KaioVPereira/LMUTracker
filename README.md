# LMUTracker

Portal de estatísticas de **Le Mans Ultimate** (parecido com o racecenter.fr): login próprio e vínculo
opcional da conta Steam. Os dados de corrida vêm de um **scanner** que varre a agenda do RaceOS
(`raceos.gg`, backend oficial da Motorsport Games) e guarda os resultados.

> Não afiliado à Motorsport Games nem ao Le Mans Ultimate. A API do RaceOS é não oficial e pode mudar a
> cada atualização do jogo.

## Estrutura (`backend/`)

Arquitetura em camadas (.NET 10), no mesmo padrão do GameTracker:

- `LmuTracker.Domain` — entidades (usuário, evento, split, resultado, piloto).
- `LmuTracker.Application` — contratos/DTOs.
- `LmuTracker.Infrastructure` — EF Core (SQL Server), cliente do RaceOS e o scanner.
- `LmuTracker.Api` — API web (login JWT + dashboard). _Em construção._
- `LmuTracker.Worker` — o scanner, rodando em loop.

## Scanner

Fluxo: descobre as corridas da agenda (`daily/list/{tier}`) e guarda o `eventId` cedo; quando a corrida
termina, baixa todos os splits (`result?eventId=&splitNo=`) e grava resultados + índice de pilotos.
Como a agenda só mostra corridas futuras, **o scanner precisa ficar rodando** para não perder histórico.

### Rodar em desenvolvimento (jogo aberto)

Com o LMU aberto na conta de serviço (`RaceOs:TicketProvider = localgame`):

```bash
cd backend
dotnet run --project LmuTracker.Worker            # inicia o scanner (loop)
dotnet run --project LmuTracker.Worker -- report  # status do que foi guardado
```

### Rodar no servidor (headless, via SteamKit2)

1. Bootstrap único (interativo) para gerar o refresh token da conta de serviço:
   ```bash
   dotnet run --project LmuTracker.Worker -- steam-login
   ```
2. Com `RaceOs:TicketProvider = steamkit`, o Worker passa a logar sozinho (sem jogo, sem Steam Guard).

## Configuração (`appsettings.json`)

- `ConnectionStrings:Default` — SQL Server.
- `RaceOs:TicketProvider` — `localgame` (dev) ou `steamkit` (servidor).
- `RaceOs:SteamKit:*` — conta de serviço e caminho do refresh token (**arquivo sensível**).
- `Scanner:IntervalSeconds` — intervalo entre passadas (padrão 120).

## Banco

EF Core + SQL Server. Migrations em `LmuTracker.Infrastructure/Persistence/Migrations`; o Worker aplica
no start (`MigrateAsync`).
