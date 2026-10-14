using LmuTracker.Infrastructure.Persistence;
using LmuTracker.Infrastructure.Scanning;
using Microsoft.EntityFrameworkCore;

namespace LmuTracker.Worker;

/// <summary>
/// Roda o scanner em loop. Cada passada abre um escopo próprio (DbContext novo), descobre a agenda e
/// baixa as corridas que terminaram. O intervalo é curto porque a janela da agenda é o que não dá para
/// recuperar: perder um eventId antes de guardá-lo é perder aquela corrida.
/// </summary>
public class Worker : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<Worker> _log;
    private readonly TimeSpan _interval;

    public Worker(IServiceProvider sp, ILogger<Worker> log, IConfiguration config)
    {
        _sp = sp;
        _log = log;
        _interval = TimeSpan.FromSeconds(config.GetValue("Scanner:IntervalSeconds", 120));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Garante o banco criado/migrado antes de começar.
        using (var scope = _sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Migrations que criam índice percorrem a tabela de resultados inteira — bem mais que os 30 s padrão.
            db.Database.SetCommandTimeout(TimeSpan.FromMinutes(15));
            await db.Database.MigrateAsync(stoppingToken);
        }

        _log.LogInformation("Scanner iniciado (intervalo {Interval}s).", _interval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _sp.CreateScope();
                var scanner = scope.ServiceProvider.GetRequiredService<ScannerService>();
                await scanner.RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Scanner: passada falhou; tentando de novo no próximo ciclo.");
            }

            try { await Task.Delay(_interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
