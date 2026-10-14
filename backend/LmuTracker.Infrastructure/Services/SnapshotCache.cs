using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using LmuTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LmuTracker.Infrastructure.Services;

/// <summary>
/// Cache das agregações pesadas (início, rankings, pistas, carros). Diferente de um cache que expira:
/// <list type="bullet">
/// <item>vencido, devolve na hora o último resultado e recalcula em segundo plano — ninguém espera a consulta
/// depois da primeira vez;</item>
/// <item>o cálculo roda num escopo (DbContext) próprio e não é cancelado se quem pediu desistir — antes, uma
/// consulta que estourava o tempo do cliente nunca chegava ao cache e toda visita tentava de novo;</item>
/// <item>pedidos simultâneos da mesma chave esperam o mesmo cálculo (uma consulta só).</item>
/// </list>
/// Chaves sem leitura há mais de <see cref="IdleFor"/> saem da memória (filtros raros, como país × classe).
/// </summary>
public sealed class SnapshotCache
{
    private static readonly TimeSpan IdleFor = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan SweepEvery = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(3);

    private sealed class Slot
    {
        public readonly object Gate = new();
        public bool HasValue;
        public object? Value;
        public DateTime At;
        public DateTime LastRead;
        public Task? Running;
        public ExceptionDispatchInfo? Error;
    }

    private readonly ConcurrentDictionary<string, Slot> _slots = new();
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<SnapshotCache> _log;
    private DateTime _lastSweep = DateTime.UtcNow;

    public SnapshotCache(IServiceScopeFactory scopes, ILogger<SnapshotCache> log)
    {
        _scopes = scopes;
        _log = log;
    }

    /// <summary>
    /// Valor da chave: o guardado se tiver menos de <paramref name="freshFor"/>; vencido, o guardado (e um recálculo
    /// em segundo plano); sem nada guardado, espera o cálculo. <paramref name="ct"/> só cancela a espera.
    /// </summary>
    public async Task<T> GetAsync<T>(string key, TimeSpan freshFor, Func<AppDbContext, CancellationToken, Task<T>> build,
        CancellationToken ct = default)
    {
        Sweep();
        var slot = _slots.GetOrAdd(key, _ => new Slot());
        Task running;
        lock (slot.Gate)
        {
            var now = DateTime.UtcNow;
            slot.LastRead = now;
            if (slot.HasValue && now - slot.At < freshFor) return (T)slot.Value!;
            running = slot.Running ??= Start(slot, key, build);
            if (slot.HasValue) return (T)slot.Value!;
        }

        await running.WaitAsync(ct);
        lock (slot.Gate)
        {
            if (slot.HasValue) return (T)slot.Value!;
            slot.Error?.Throw();
            throw new InvalidOperationException($"Cache '{key}' sem valor.");
        }
    }

    /// <summary>Recalcula em segundo plano se estiver vencido (aquecimento); não espera.</summary>
    public void Warm<T>(string key, TimeSpan freshFor, Func<AppDbContext, CancellationToken, Task<T>> build)
    {
        var slot = _slots.GetOrAdd(key, _ => new Slot());
        lock (slot.Gate)
        {
            if (slot.LastRead == default) slot.LastRead = DateTime.UtcNow;
            if (slot.HasValue && DateTime.UtcNow - slot.At < freshFor) return;
            slot.Running ??= Start(slot, key, build);
        }
    }

    // Chamado com o Gate do slot travado; o Task.Run garante que o fim do cálculo (que trava o Gate de novo)
    // só acontece depois de Running ter sido atribuído.
    private Task Start<T>(Slot slot, string key, Func<AppDbContext, CancellationToken, Task<T>> build) =>
        Task.Run(async () =>
        {
            var started = DateTime.UtcNow;
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Database.SetCommandTimeout(BuildTimeout); // ninguém está esperando: pode demorar mais que um request
                var value = await build(db, CancellationToken.None);
                lock (slot.Gate)
                {
                    slot.Value = value;
                    slot.HasValue = true;
                    slot.At = DateTime.UtcNow;
                    slot.Error = null;
                    slot.Running = null;
                }
                var took = DateTime.UtcNow - started;
                if (took > TimeSpan.FromSeconds(5)) _log.LogInformation("Cache {Key} recalculado em {Ms} ms", key, (int)took.TotalMilliseconds);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Falha ao calcular o cache {Key}", key);
                lock (slot.Gate)
                {
                    slot.Error = ExceptionDispatchInfo.Capture(ex);
                    slot.Running = null;
                }
            }
        });

    private void Sweep()
    {
        var now = DateTime.UtcNow;
        if (now - _lastSweep < SweepEvery) return;
        _lastSweep = now;
        foreach (var (key, slot) in _slots)
        {
            lock (slot.Gate)
            {
                if (slot.Running is null && now - slot.LastRead > IdleFor) _slots.TryRemove(key, out _);
            }
        }
    }
}
