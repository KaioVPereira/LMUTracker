using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LmuTracker.Infrastructure.RaceOs;

/// <summary>
/// Cliente da API online do RaceOS para a conta de serviço. Cuida do token (ticket Steam → accessToken,
/// renovado perto do vencimento) e expõe só as chamadas read-only de que o scanner precisa.
/// </summary>
public sealed class RaceOsClient
{
    // Nomes exatos (todos os DTOs têm JsonPropertyName). NÃO ligar case-insensitive: o resultado traz "Laps"
    // (número de voltas) e "laps" (lista volta a volta) no mesmo objeto, e eles colidiriam.
    // Campos de texto aceitam número também (FlexibleStringConverter): o RaceOS manda números como texto,
    // mas uma mudança no formato não pode derrubar a leitura do resultado inteiro.
    internal static readonly JsonSerializerOptions Json = new() { Converters = { new FlexibleStringConverter() } };

    private readonly IHttpClientFactory _factory;
    private readonly ISteamTicketProvider _ticket;
    private readonly RaceOsOptions _opt;
    private readonly ILogger<RaceOsClient> _log;
    private readonly SemaphoreSlim _authLock = new(1, 1);

    private string? _token;
    private DateTime _tokenExpiresAt = DateTime.MinValue;

    public RaceOsClient(
        IHttpClientFactory factory,
        ISteamTicketProvider ticket,
        IOptions<RaceOsOptions> opt,
        ILogger<RaceOsClient> log)
    {
        _factory = factory;
        _ticket = ticket;
        _opt = opt.Value;
        _log = log;
    }

    private HttpClient Http()
    {
        var c = _factory.CreateClient("raceos");
        c.BaseAddress = new Uri(_opt.BaseUrl);
        return c;
    }

    // ---------- Autenticação ----------

    private async Task<string> EnsureTokenAsync(CancellationToken ct)
    {
        if (_token is not null && DateTime.UtcNow < _tokenExpiresAt.AddMinutes(-2))
            return _token;

        await _authLock.WaitAsync(ct);
        try
        {
            if (_token is not null && DateTime.UtcNow < _tokenExpiresAt.AddMinutes(-2))
                return _token;

            var ticket = await _ticket.GetTicketAsync(ct);
            using var resp = await Http().PostAsJsonAsync(
                "/authenticate", new { token = ticket, game = "lmu", platform = "steam" }, ct);
            resp.EnsureSuccessStatusCode();

            var auth = await ReadJsonAsync<AuthResult>(resp, ct)
                ?? throw new InvalidOperationException("Resposta de /authenticate vazia.");
            if (string.IsNullOrEmpty(auth.AccessToken))
                throw new InvalidOperationException("RaceOS não devolveu accessToken.");
            if (auth.Banned)
                _log.LogWarning("Conta de serviço marcada como banida pelo RaceOS.");

            _token = auth.AccessToken;
            _tokenExpiresAt = auth.ExpiresAt ?? DateTime.UtcNow.AddMinutes(25);
            _log.LogInformation("RaceOS autenticado; token válido até {Expiry:u}.", _tokenExpiresAt);
            return _token;
        }
        finally
        {
            _authLock.Release();
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var token = await EnsureTokenAsync(ct);
        using var req = new HttpRequestMessage(method, path);
        req.Headers.TryAddWithoutValidation("Game-Authorization", "Bearer " + token);
        if (body is not null)
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return await Http().SendAsync(req, ct);
    }

    /// <summary>
    /// Lê o corpo inteiro antes de desserializar: com o JSON completo em memória o conversor tolerante consegue
    /// pular campos de formato inesperado (em streaming, Skip() falha no meio de um bloco parcial).
    /// </summary>
    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage resp, CancellationToken ct)
    {
        var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
        return JsonSerializer.Deserialize<T>(bytes, Json);
    }

    // ---------- Descoberta ----------

    /// <summary>Próximas corridas de um tier (beginner|intermediate|advanced), cada uma já com o eventId.</summary>
    public async Task<IReadOnlyList<RaceOsEvent>> ListTierAsync(string tier, int take, CancellationToken ct)
    {
        using var resp = await SendAsync(HttpMethod.Get, $"/api/v1/daily/list/{tier}?take={take}", null, ct);
        if (!resp.IsSuccessStatusCode)
        {
            _log.LogWarning("daily/list/{Tier} => HTTP {Code}", tier, (int)resp.StatusCode);
            return Array.Empty<RaceOsEvent>();
        }
        return await ReadJsonAsync<List<RaceOsEvent>>(resp, ct) ?? new();
    }

    /// <summary>Séries de <c>special-events/{kind}</c> (kind = weekly | special), cada uma com suas instâncias.</summary>
    public async Task<IReadOnlyList<RaceOsSeries>> ListSpecialAsync(string kind, CancellationToken ct)
    {
        using var resp = await SendAsync(HttpMethod.Get, $"/api/v1/special-events/{kind}", null, ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"special-events/{kind} => HTTP {(int)resp.StatusCode}");
        return await ReadJsonAsync<List<RaceOsSeries>>(resp, ct) ?? new();
    }

    // ---------- Expansão ----------

    /// <summary>Metadados do evento, inclusive <c>totalSplits</c>. Falha HTTP vira exceção (o scanner tenta de novo).</summary>
    public async Task<OverviewResult> GetOverviewAsync(string eventType, string eventId, CancellationToken ct)
    {
        using var resp = await SendAsync(HttpMethod.Post, "/api/v1/event/overview",
            new { game = "lmu", eventType, eventId }, ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"event/overview {eventId} => HTTP {(int)resp.StatusCode}");
        return await ReadJsonAsync<OverviewResult>(resp, ct)
            ?? throw new InvalidOperationException($"event/overview {eventId} veio vazio");
    }

    /// <summary>
    /// Resultado de um split. Null SÓ em 404 (resultado ainda não publicado / split inexistente); qualquer
    /// outra falha HTTP vira exceção, para não ser confundida com "acabaram os splits".
    /// </summary>
    public async Task<SplitResult?> GetResultAsync(string eventId, int splitNo, CancellationToken ct)
    {
        using var resp = await SendAsync(HttpMethod.Get,
            $"/api/v1/result?eventId={Uri.EscapeDataString(eventId)}&splitNo={splitNo}", null, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"result {eventId} split {splitNo} => HTTP {(int)resp.StatusCode}");
        return await ReadJsonAsync<SplitResult>(resp, ct);
    }
}
