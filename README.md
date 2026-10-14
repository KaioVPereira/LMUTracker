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

## App Android

Mesmo esquema do Finanças e do Deu Quanto: Capacitor 8 com o front empacotado no APK (`frontend/android`,
`com.gamestrackers.pitwall`), chamando a Api de produção pelo endereço completo (build `--mode mobile`). O app
avisa sozinho quando sai versão nova; no navegador do Android o site sugere baixar o app; a página `/app` tem o
download.

```powershell
cd frontend
npm run apk        # build mobile + cap sync + gradlew assembleDebug
npm run icons      # regera ícone e splash (scripts/make-icons.mjs)
```

O APK sai em `frontend/android/app/build/outputs/apk/debug/app-debug.apk`.

### Publicar uma versão

A versão mais nova fica na tabela `AppReleases`; o app compara com o próprio `versionCode` e mostra o aviso.

1. Subir `versionCode` (inteiro, sempre maior) e `versionName` em `frontend/android/app/build.gradle`.
2. `npm run apk` e copiar o APK para `apk/pitwall-X.Y.Z.apk`.
3. `scp apk/pitwall-X.Y.Z.apk root@<vps>:/root/lmutracker-data/apk/`
4. `ssh root@<vps> "cd /root/LMUTracker/deploy && docker compose exec worker dotnet LmuTracker.Worker.dll publish-release --version X.Y.Z --code N --file pitwall-X.Y.Z.apk --notes 'O que mudou'"`
   (em `--notes`, `\n` vira quebra de linha).
5. Conferir `https://lmutracker.gamestrackers.com/api/app/android`.

No app, o vínculo com a Steam abre dentro do próprio app e volta para `https://localhost/vincular/retorno`
(`Site:AppOrigin`), onde a sessão já está.
