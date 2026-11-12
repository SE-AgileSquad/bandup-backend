using BandUp.Domain.Billing;
using BandUp.Domain.Content;
using BandUp.Domain.Identity;
using BandUp.Domain.Learning;
using BandUp.Domain.Vocabulary;
using BandUp.Infrastructure.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using BandUp.Domain.Exams;

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

    public DbSet<Lesson> Lessons => Set<Lesson>();

    public DbSet<LessonExample> LessonExamples => Set<LessonExample>();

    public DbSet<LessonMap> LessonMaps => Set<LessonMap>();

    public DbSet<LessonProgress> LessonProgressEntries => Set<LessonProgress>();

    public DbSet<ParaphrasePair> ParaphrasePairs => Set<ParaphrasePair>();

    public DbSet<VocabCard> VocabCards => Set<VocabCard>();

    public DbSet<ReviewItem> ReviewItems => Set<ReviewItem>();

    public DbSet<Attempt> Attempts => Set<Attempt>();

    public DbSet<AttemptAnswer> AttemptAnswers => Set<AttemptAnswer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BandUpDbContext).Assembly);

        SnakeCaseNaming.Apply(modelBuilder.Model);
    }
}
