using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SteamKit2;

namespace LmuTracker.Infrastructure.RaceOs;

/// <summary>
/// Emite o ticket Web API da conta de serviço sem o jogo aberto. Loga na Steam com o refresh token guardado
/// pelo bootstrap e, a cada pedido, gera um ticket Web API para o appid do LMU.
/// <para>
/// A conexão é sob demanda: só reconecta quando alguém está esperando um login. Uma falha definitiva
/// (token expirado/inválido) não vira laço de tentativas — a próxima tentativa vem no próximo pedido, no
/// ritmo do scanner. Resultados transitórios da Steam (TryAnotherCM etc.) apenas trocam de servidor.
/// </para>
/// </summary>
public sealed class SteamKitTicketProvider : ISteamTicketProvider, IDisposable
{
    private static readonly TimeSpan LogonTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    /// <summary>Pausa depois de falha definitiva, para não martelar a Steam (rate limit) com token inválido.</summary>
    private static readonly TimeSpan FatalCooldown = TimeSpan.FromMinutes(15);
    private DateTime _fatalUntil = DateTime.MinValue;
    private string? _fatalReason;

    private readonly SteamKitOptions _opt;
    private readonly SteamSessionStore _store;
    private readonly ILogger<SteamKitTicketProvider> _log;

    private readonly SteamClient _client = new();
    private readonly CallbackManager _manager;
    private readonly SteamUser _steamUser;
    private readonly SteamAuthTicket _ticket;

    private readonly object _gate = new();
    private Thread? _loop;
    private volatile bool _running;
    private volatile bool _loggedOn;
    private bool _connected;
    private bool _connectPending;
    private TaskCompletionSource<bool> _logon = NewSignal();
    private SteamAuthTicket.TicketInfo? _lastTicket;

    public SteamKitTicketProvider(
        IOptions<SteamKitOptions> opt, SteamSessionStore store, ILogger<SteamKitTicketProvider> log)
    {
        _opt = opt.Value;
        _store = store;
        _log = log;
        _manager = new CallbackManager(_client);
        _steamUser = _client.GetHandler<SteamUser>()!;
        _ticket = _client.GetHandler<SteamAuthTicket>()!;

        _manager.Subscribe<SteamClient.ConnectedCallback>(OnConnected);
        _manager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);
        _manager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
        _manager.Subscribe<SteamUser.LoggedOffCallback>(OnLoggedOff);
    }

    private static TaskCompletionSource<bool> NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<string> GetTicketAsync(CancellationToken ct = default)
    {
        await EnsureLoggedOnAsync(ct);

        var info = await _ticket.GetAuthTicketForWebApi(_opt.AppId, _opt.WebApiIdentity);

        // Não descartamos na hora: o Dispose cancela o ticket na Steam e o RaceOS ainda vai validá-lo.
        // Guardamos o último e descartamos o anterior na próxima emissão.
        var previous = Interlocked.Exchange(ref _lastTicket, info);
        previous?.Dispose();

        return Convert.ToHexString(info.Ticket).ToLowerInvariant();
    }

    private async Task EnsureLoggedOnAsync(CancellationToken ct)
    {
        if (_loggedOn) return;

        if (DateTime.UtcNow < _fatalUntil)
            throw new InvalidOperationException(
                $"{_fatalReason} Nova tentativa a partir de {_fatalUntil:HH:mm} UTC.");

        TaskCompletionSource<bool> signal;
        lock (_gate)
        {
            StartLoop();
            // Um sinal já resolvido (sucesso antigo ou falha) não serve para esta espera: começa outro.
            if (_logon.Task.IsCompleted) _logon = NewSignal();
            signal = _logon;

            if (!_connected && !_connectPending)
            {
                _connectPending = true;
                _log.LogInformation("SteamKit: conectando à Steam…");
                _client.Connect();
            }
            else if (_connected)
            {
                LogOnWithStoredSession(); // conectado mas deslogado (ex.: após LoggedOff)
            }
        }

        // WaitAsync estoura sem contaminar o sinal compartilhado.
        await signal.Task.WaitAsync(LogonTimeout, ct);
    }

    private void StartLoop()
    {
        if (_running) return;
        _running = true;
        _loop = new Thread(CallbackLoop) { IsBackground = true, Name = "steamkit-callbacks" };
        _loop.Start();
    }

    private void CallbackLoop()
    {
        while (_running)
        {
            try { _manager.RunWaitCallbacks(TimeSpan.FromSeconds(1)); }
            catch (Exception ex) { _log.LogError(ex, "SteamKit: erro no loop de callbacks."); }
        }
    }

    private void OnConnected(SteamClient.ConnectedCallback _)
    {
        lock (_gate)
        {
            _connected = true;
            _connectPending = false;
            LogOnWithStoredSession();
        }
    }

    private void LogOnWithStoredSession()
    {
        var session = _store.Load();
        if (session is null)
        {
            _log.LogError("SteamKit: sem sessão guardada. Gere com scripts\\steam-login-servidor.bat (ou `-- steam-login`).");
            _logon.TrySetException(new InvalidOperationException(
                "Sessão Steam ausente. Gere o refresh token com o bootstrap steam-login."));
            return;
        }

        _log.LogInformation("SteamKit: logando como {Account} com o refresh token guardado.", session.AccountName);
        _steamUser.LogOn(new SteamUser.LogOnDetails
        {
            Username = session.AccountName,
            AccessToken = session.RefreshToken,
            ShouldRememberPassword = true,
        });
    }

    private void OnLoggedOn(SteamUser.LoggedOnCallback cb)
    {
        if (cb.Result == EResult.OK)
        {
            _loggedOn = true;
            _fatalUntil = DateTime.MinValue;
            _logon.TrySetResult(true);
            _log.LogInformation("SteamKit: logado na Steam como conta de serviço.");
            return;
        }

        if (cb.Result is EResult.TryAnotherCM or EResult.ServiceUnavailable or EResult.Timeout
            or EResult.Busy or EResult.ServiceReadOnly)
        {
            // Transitório: a Steam derruba a conexão e reconectamos em outro servidor (ver OnDisconnected).
            _log.LogWarning("SteamKit: login recusado de forma temporária ({Result}); trocando de servidor.", cb.Result);
            return;
        }

        _log.LogError("SteamKit: falha ao logar: {Result} / {Extended}. Se o token expirou, gere outro com o .bat.",
            cb.Result, cb.ExtendedResult);
        _fatalReason = $"Login Steam falhou: {cb.Result}. Se o refresh token expirou, gere outro com o bootstrap.";
        _fatalUntil = DateTime.UtcNow.Add(FatalCooldown);
        _logon.TrySetException(new InvalidOperationException(_fatalReason));
    }

    private void OnLoggedOff(SteamUser.LoggedOffCallback cb)
    {
        _loggedOn = false;
        _log.LogWarning("SteamKit: deslogado pela Steam ({Result}).", cb.Result);
    }

    private void OnDisconnected(SteamClient.DisconnectedCallback _)
    {
        bool reconnect;
        lock (_gate)
        {
            _loggedOn = false;
            _connected = false;
            // Só reconecta sozinho se alguém está esperando o login (caso transitório). Sem ninguém esperando,
            // a próxima chamada a GetTicketAsync reconecta — sem laço de tentativas.
            reconnect = _running && !_logon.Task.IsCompleted;
            _connectPending = reconnect;
        }

        if (!reconnect)
        {
            _log.LogInformation("SteamKit: desconectado; reconecta no próximo pedido.");
            return;
        }

        _log.LogWarning("SteamKit: desconectado durante o login; reconectando em {Delay}s.", ReconnectDelay.TotalSeconds);
        Task.Delay(ReconnectDelay).ContinueWith(_ => { if (_running) _client.Connect(); });
    }

    public void Dispose()
    {
        _running = false;
        _lastTicket?.Dispose();
        try { _client.Disconnect(); } catch { /* ignore */ }
    }
}
