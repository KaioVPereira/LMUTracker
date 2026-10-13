using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LmuTracker.Infrastructure.Persistence;

/// <summary>
/// Usado só pelas ferramentas do EF (migrations) em tempo de design, para criar o contexto sem subir o
/// Worker. A string vem de LMUTRACKER_DB quando definida; senão, LocalDB de desenvolvimento.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("LMUTRACKER_DB")
                 ?? "Server=(localdb)\\MSSQLLocalDB;Database=LmuTracker;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(cs)
            .Options;

        return new AppDbContext(options);
    }
}
