@echo off
setlocal
title LMUTracker - Login Steam e envio da sessao ao servidor

REM Gera a sessao Steam NO SEU PC (a Steam confia na sua localizacao), envia ao servidor,
REM instala no volume do worker e reinicia o scanner. A senha nunca sai do seu PC:
REM so o refresh token (steam-session.json) vai para o servidor.
REM
REM O servidor vem da variavel LMU_SERVER (ex.: root@1.2.3.4). Para nao digitar sempre:
REM     setx LMU_SERVER root@SEU_IP

set "REPO=%~dp0.."
set "SESSION=%USERPROFILE%\steam-session.json"

if "%LMU_SERVER%"=="" set /p "LMU_SERVER=Servidor SSH (ex.: root@1.2.3.4): "
if "%LMU_SERVER%"=="" (
  echo Servidor nao informado.
  goto :fim
)

echo.
echo [0/3] Conferindo o .env no servidor...
ssh %LMU_SERVER% "cd /root/LMUTracker/deploy && if grep -q -e A_MESMA_SENHA_DO_GAMETRACKER -e conta_de_servico .env; then echo 'O .env ainda tem placeholders: preencha a senha do SA e o STEAM_USERNAME.'; exit 1; fi"
if errorlevel 1 (
  echo Ajuste o .env no servidor e rode este script de novo.
  goto :fim
)

echo.
echo [1/3] Login na Steam. Digite o login, a senha e o Steam Guard quando pedir.
if exist "%SESSION%" del "%SESSION%"
pushd "%REPO%\backend"
set "RaceOs__SteamKit__SessionStorePath=%SESSION%"
dotnet run --project LmuTracker.Worker -- steam-login
popd
if not exist "%SESSION%" (
  echo A sessao nao foi gerada. Nada foi enviado.
  goto :fim
)

echo.
echo [2/3] Enviando a sessao para o servidor...
scp "%SESSION%" %LMU_SERVER%:/root/steam-session.json
if errorlevel 1 (
  echo Falha ao enviar. A sessao continua em %SESSION%.
  goto :fim
)

echo.
echo [3/3] Instalando no volume e reiniciando o worker (aguarde ~30s)...
ssh %LMU_SERVER% "cd /root/LMUTracker/deploy && (docker volume inspect lmutracker_steam-session >/dev/null 2>&1 || docker volume create lmutracker_steam-session >/dev/null) && cp /root/steam-session.json /var/lib/docker/volumes/lmutracker_steam-session/_data/steam-session.json && rm -f /root/steam-session.json && docker compose up -d --force-recreate worker && sleep 30 && docker compose logs --tail 25 worker"
if errorlevel 1 (
  echo Falha no servidor. Veja as mensagens acima.
  goto :fim
)

del "%SESSION%"
echo.
echo Pronto. A copia local da sessao foi apagada.
echo Procure acima por "RaceOS autenticado" e "novas corridas na agenda guardadas".

:fim
echo.
pause
endlocal
