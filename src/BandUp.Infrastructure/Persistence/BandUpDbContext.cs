using BandUp.Domain.Billing;
using BandUp.Domain.Content;
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

    public DbSet<CreditAccount> CreditAccounts => Set<CreditAccount>();

    public DbSet<CreditLedgerEntry> CreditLedger => Set<CreditLedgerEntry>();

    public DbSet<Passage> Passages => Set<Passage>();

    public DbSet<Question> Questions => Set<Question>();

    public DbSet<AnswerKey> AnswerKeys => Set<AnswerKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BandUpDbContext).Assembly);

        SnakeCaseNaming.Apply(modelBuilder.Model);
    }
}
