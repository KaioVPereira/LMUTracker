using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SteamKit2;
using SteamKit2.Authentication;

namespace LmuTracker.Infrastructure.RaceOs;

/// <summary>
/// Emite o ticket Web API da conta de serviço sem o jogo aberto. Mantém uma sessão Steam logada (via refresh
/// token guardado pelo bootstrap) num loop de callbacks próprio e, a cada pedido, gera um ticket Web API para
/// o appid do LMU. Se não houver sessão guardada, orienta a rodar o bootstrap (`steam-login`).
/// </summary>
public sealed class SteamKitTicketProvider : ISteamTicketProvider, IDisposable
{
    private readonly SteamKitOptions _opt;
    private readonly SteamSessionStore _store;
    private readonly ILogger<SteamKitTicketProvider> _log;

    private readonly SteamClient _client = new();
    private readonly CallbackManager _manager;
    private readonly SteamUser _steamUser;
    private readonly SteamAuthTicket _ticket;

    private readonly object _startLock = new();
    private Thread? _loop;
    private volatile bool _running;
    private volatile bool _loggedOn;
    private TaskCompletionSource<bool> _logon = new(TaskCreationOptions.RunContinuationsAsynchronously);
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
    }

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

    private void EnsureStarted()
    {
        if (_running) return;
        lock (_startLock)
        {
            if (_running) return;
            _running = true;
            _loop = new Thread(CallbackLoop) { IsBackground = true, Name = "steamkit-callbacks" };
            _loop.Start();
            _client.Connect();
            _log.LogInformation("SteamKit: conectando à Steam…");
        }
    }

    private async Task EnsureLoggedOnAsync(CancellationToken ct)
    {
        if (_loggedOn) return;
        EnsureStarted();

        var tcs = _logon;
        using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var reg2 = timeout.Token.Register(() =>
            tcs.TrySetException(new TimeoutException("SteamKit: tempo esgotado ao logar na Steam.")));

        await tcs.Task;
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
        var session = _store.Load();
        if (session is null)
        {
            _log.LogError("SteamKit: sem sessão guardada. Rode o bootstrap: `dotnet run --project LmuTracker.Worker -- steam-login`.");
            _logon.TrySetException(new InvalidOperationException(
                "Sessão Steam ausente. Rode o bootstrap `steam-login` uma vez para gerar o refresh token."));
            return;
        }

        _log.LogInformation("SteamKit: conectado; logando como {Account} com o refresh token guardado.", session.AccountName);
        _steamUser.LogOn(new SteamUser.LogOnDetails
        {
            Username = session.AccountName,
            AccessToken = session.RefreshToken,
            ShouldRememberPassword = true,
        });
    }

    private void OnLoggedOn(SteamUser.LoggedOnCallback cb)
    {
        if (cb.Result != EResult.OK)
        {
            _log.LogError("SteamKit: falha ao logar: {Result} / {Extended}.", cb.Result, cb.ExtendedResult);
            _loggedOn = false;
            _logon.TrySetException(new InvalidOperationException(
                $"Login Steam falhou: {cb.Result}. Se o refresh token expirou, rode `steam-login` de novo."));
            return;
        }

        _loggedOn = true;
        _logon.TrySetResult(true);
        _log.LogInformation("SteamKit: logado na Steam como conta de serviço.");
    }

    private void OnDisconnected(SteamClient.DisconnectedCallback _)
    {
        _loggedOn = false;
        // Prepara um novo sinal para a próxima tentativa.
        _logon = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_running) return;

        _log.LogWarning("SteamKit: desconectado; reconectando em 5s.");
        Task.Delay(TimeSpan.FromSeconds(5)).ContinueWith(_ => { if (_running) _client.Connect(); });
    }

    public void Dispose()
    {
        _running = false;
        _lastTicket?.Dispose();
        try { _client.Disconnect(); } catch { /* ignore */ }
    }
}
