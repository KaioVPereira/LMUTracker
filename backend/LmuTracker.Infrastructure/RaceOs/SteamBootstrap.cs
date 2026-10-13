using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SteamKit2;
using SteamKit2.Authentication;

namespace LmuTracker.Infrastructure.RaceOs;

/// <summary>
/// Login interativo único (console) da conta de serviço: pede senha + Steam Guard e guarda o refresh token.
/// Depois disso o servidor loga sozinho com <see cref="SteamKitTicketProvider"/>, sem senha nem 2FA.
/// </summary>
public sealed class SteamBootstrap
{
    private readonly SteamKitOptions _opt;
    private readonly SteamSessionStore _store;
    private readonly ILogger<SteamBootstrap> _log;

    public SteamBootstrap(IOptions<SteamKitOptions> opt, SteamSessionStore store, ILogger<SteamBootstrap> log)
    {
        _opt = opt.Value;
        _store = store;
        _log = log;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var user = _opt.Username;
        if (string.IsNullOrWhiteSpace(user))
        {
            Console.Write("Conta Steam de serviço (login): ");
            user = (Console.ReadLine() ?? "").Trim();
        }
        Console.Write($"Senha de '{user}': ");
        var pass = ReadSecret();

        var existing = _store.Load();
        var client = new SteamClient();
        var manager = new CallbackManager(client);
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        manager.Subscribe<SteamClient.ConnectedCallback>(async _ =>
        {
            try
            {
                var session = await client.Authentication.BeginAuthSessionViaCredentialsAsync(new AuthSessionDetails
                {
                    Username = user,
                    Password = pass,
                    IsPersistentSession = true,
                    GuardData = existing?.GuardData,
                    Authenticator = new UserConsoleAuthenticator(),
                });

                var poll = await session.PollingWaitForResultAsync(ct);
                _store.Save(new SteamSession(poll.AccountName, poll.RefreshToken, poll.NewGuardData ?? existing?.GuardData));
                Console.WriteLine($"\nSessão salva para '{poll.AccountName}'. O servidor já pode logar sozinho.");
                done.TrySetResult(true);
            }
            catch (Exception ex)
            {
                done.TrySetException(ex);
            }
            finally
            {
                client.Disconnect();
            }
        });
        manager.Subscribe<SteamClient.DisconnectedCallback>(_ => done.TrySetResult(done.Task.IsCompleted));

        client.Connect();

        using var reg = ct.Register(() => done.TrySetCanceled(ct));
        while (!done.Task.IsCompleted)
            manager.RunWaitCallbacks(TimeSpan.FromMilliseconds(200));

        await done.Task; // propaga erro, se houve
    }

    /// <summary>Leitura mascarada de senha, com fallback para ReadLine quando o console não permite.</summary>
    private static string ReadSecret()
    {
        try
        {
            var pass = "";
            ConsoleKeyInfo k;
            while ((k = Console.ReadKey(intercept: true)).Key != ConsoleKey.Enter)
            {
                if (k.Key == ConsoleKey.Backspace && pass.Length > 0) pass = pass[..^1];
                else if (!char.IsControl(k.KeyChar)) pass += k.KeyChar;
            }
            Console.WriteLine();
            return pass;
        }
        catch (InvalidOperationException)
        {
            return (Console.ReadLine() ?? "").Trim();
        }
    }
}
