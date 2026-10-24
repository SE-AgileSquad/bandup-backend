using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BandUp.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef</c> build the model without starting the API. Adding a migration does not
/// open a connection; only applying one does, and then <c>ConnectionStrings__Default</c> wins.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<BandUpDbContext>
{
    private const string LocalFallback =
        "Host=localhost;Port=5432;Database=bandup;Username=postgres;Password=postgres";

    public BandUpDbContext CreateDbContext(string[] args)
    {
        string connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default") ?? LocalFallback;

        DbContextOptionsBuilder<BandUpDbContext> options = new();

        options.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable(
                BandUpDbContext.MigrationsHistoryTable,
                BandUpDbContext.MigrationsHistorySchema));

        return new BandUpDbContext(options.Options);
    }
}