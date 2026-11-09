using BandUp.Domain.Identity;
using BandUp.Infrastructure.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;

namespace BandUp.Infrastructure.Persistence;

public sealed class BandUpDbContext(DbContextOptions<BandUpDbContext> options) : DbContext(options)
{
    public const string MigrationsHistoryTable = "__EFMigrationsHistory";
    public const string MigrationsHistorySchema = "public";

    public DbSet<User> Users => Set<User>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BandUpDbContext).Assembly);

        SnakeCaseNaming.Apply(modelBuilder.Model);
    }
}