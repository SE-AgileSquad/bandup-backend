using BandUp.Domain.Billing;
using BandUp.Domain.Content;
using BandUp.Domain.Identity;
using BandUp.Domain.Learning;
using BandUp.Domain.Vocabulary;
using BandUp.Infrastructure.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using BandUp.Domain.Exams;
using BandUp.Domain.Writing;

namespace BandUp.Infrastructure.Persistence;

public sealed class BandUpDbContext(DbContextOptions<BandUpDbContext> options, TimeProvider? clock = null) : DbContext(options)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public const string MigrationsHistoryTable = "__EFMigrationsHistory";
    public const string MigrationsHistorySchema = "public";

    public DbSet<User> Users => Set<User>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<PasswordResetChallenge> PasswordResetChallenges => Set<PasswordResetChallenge>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<OtpLoginChallenge> OtpLoginChallenges => Set<OtpLoginChallenge>();

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

    public DbSet<WritingPrompt> WritingPrompts => Set<WritingPrompt>();

    public DbSet<WritingSubmission> WritingSubmissions => Set<WritingSubmission>();

    public DbSet<WritingFeedback> WritingFeedbacks => Set<WritingFeedback>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampPassageUpdates();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampPassageUpdates();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    internal void MarkPassageUpdated(Guid passageId)
    {
        Passage passage = Passages.Local.Single(item => item.Id == passageId);
        Entry(passage).Property(item => item.UpdatedAt).CurrentValue = DatabaseTimestamp();
    }

    private void StampPassageUpdates()
    {
        ChangeTracker.DetectChanges();
        HashSet<Guid> changedKeyQuestions = ChangeTracker.Entries<AnswerKey>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(entry => entry.Entity.QuestionId).ToHashSet();
        HashSet<Guid> changedPassages = ChangeTracker.Entries<Question>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                || changedKeyQuestions.Contains(entry.Entity.Id))
            .Select(entry => entry.Entity.PassageId).ToHashSet();

        DateTimeOffset now = DatabaseTimestamp();
        foreach (var entry in ChangeTracker.Entries<Passage>().ToArray())
        {
            if (entry.State is EntityState.Added or EntityState.Modified
                || (entry.State == EntityState.Unchanged && changedPassages.Contains(entry.Entity.Id)))
            {
                entry.Property(passage => passage.UpdatedAt).CurrentValue = now;
            }
        }
    }

    private DateTimeOffset DatabaseTimestamp()
    {
        DateTimeOffset now = _clock.GetUtcNow();
        // PostgreSQL timestamps keep microseconds; .NET clocks can also return 100 ns ticks.
        return now.AddTicks(-(now.Ticks % 10));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BandUpDbContext).Assembly);

        SnakeCaseNaming.Apply(modelBuilder.Model);
    }
}
