using BandUp.Domain.Billing;
using BandUp.Domain.Content;
using BandUp.Domain.Identity;
using BandUp.Domain.Learning;
using BandUp.Domain.Vocabulary;
using BandUp.Infrastructure.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using BandUp.Domain.Exams;
using BandUp.Domain.Writing;

namespace BandUp.Infrastructure.Persistence;

public sealed class BandUpDbContext(DbContextOptions<BandUpDbContext> options, TimeProvider? clock = null) : DbContext(options)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Dictionary<WritingSubmission, WritingLifecycleChange> _writingLifecycle = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<WritingSubmission, AcceptedWritingValues> _writingSubmissionInserts = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<WritingFeedback, (Guid Id, WritingFeedbackContent Content, string Version, decimal Band)>
        _writingFeedbackInserts = new(ReferenceEqualityComparer.Instance);

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

    internal DbSet<WritingFeedback> WritingFeedbacks => Set<WritingFeedback>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampPassageUpdates();
        ValidateWritingMutations();
        var saved = base.SaveChanges(acceptAllChangesOnSuccess);
        ClearWritingAuthorizations();
        return saved;
    }

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampPassageUpdates();
        ValidateWritingMutations();
        var saved = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        ClearWritingAuthorizations();
        return saved;
    }

    // Claim and settlement authorize exact tracked values for this context's next save.
    // Recovery metadata is independent of the protected lifecycle columns.
    internal void AllowWritingLifecycleChange(WritingSubmission submission)
    {
        ChangeTracker.DetectChanges();
        _writingLifecycle[submission] = WritingLifecycleChange.Read(Entry(submission));
    }

    internal void AllowWritingFeedbackInsert(WritingFeedback feedback) =>
        _writingFeedbackInserts[feedback] = FeedbackValues(feedback);

    // Acceptance records the immutable values before any later tracked changes can reach SaveChanges.
    internal void AllowWritingSubmissionInsert(WritingSubmission submission) =>
        _writingSubmissionInserts[submission] = AcceptedWritingValues.Read(submission);

    private void ValidateWritingMutations()
    {
        foreach (var entry in ChangeTracker.Entries<WritingSubmission>())
        {
            var change = WritingLifecycleChange.Read(entry);
            var invalid = entry.State switch
            {
                EntityState.Added => !_writingSubmissionInserts.TryGetValue(entry.Entity, out var accepted) ||
                    accepted != AcceptedWritingValues.Read(entry.Entity) || change.Status != WritingEvaluationStatus.Queued ||
                    change.StartedAt is not null || change.CompletedAt is not null,
                EntityState.Modified => entry.Properties.Any(property => property.IsModified &&
                    !IsMutableWritingProperty(property.Metadata.Name)) ||
                    (change.HasProtectedWrites &&
                        (!_writingLifecycle.TryGetValue(entry.Entity, out var allowed) || change != allowed)),
                EntityState.Deleted => true,
                _ => false
            };
            if (invalid)
            {
                throw new InvalidOperationException("Unauthorized Writing submission change.");
            }
        }

        foreach (var entry in ChangeTracker.Entries<WritingFeedback>())
        {
            if (entry.State == EntityState.Unchanged)
            {
                continue;
            }
            if (entry.State != EntityState.Added || !_writingFeedbackInserts.TryGetValue(entry.Entity, out var allowed)
                || FeedbackValues(entry.Entity) != allowed)
            {
                throw new InvalidOperationException("Only Writing settlement may insert feedback, and feedback is immutable.");
            }
        }
    }

    private void ClearWritingAuthorizations()
    {
        _writingLifecycle.Clear();
        _writingSubmissionInserts.Clear();
        _writingFeedbackInserts.Clear();
    }

    private static (Guid, WritingFeedbackContent, string, decimal) FeedbackValues(WritingFeedback feedback) =>
        (feedback.Id, feedback.Content, feedback.SchemaVersion, feedback.EstimatedOverallBand);

    // Existing rows may write only recovery metadata or explicitly authorized lifecycle columns.
    private static bool IsMutableWritingProperty(string name) => name is
        nameof(WritingSubmission.PublishAttempts) or nameof(WritingSubmission.NextPublishAttemptAt) or
        nameof(WritingSubmission.PublishLeaseId) or nameof(WritingSubmission.PublishLeaseExpiresAt) or
        nameof(WritingSubmission.PublishedAt) or nameof(WritingSubmission.Status) or
        nameof(WritingSubmission.StartedAt) or nameof(WritingSubmission.CompletedAt);

    private readonly record struct AcceptedWritingValues(
        Guid Id, Guid Owner, Guid Key, WritingPromptSnapshot Prompt, string Essay,
        DateTimeOffset SubmittedAt, DateTimeOffset QueueDeadline)
    {
        public static AcceptedWritingValues Read(WritingSubmission submission) =>
            new(submission.Id, submission.UserId, submission.IdempotencyKey, submission.PromptSnapshot,
                submission.EssayText, submission.SubmittedAt, submission.QueueDeadline);
    }

    private readonly record struct WritingLifecycleChange(
        WritingEvaluationStatus Status, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt,
        bool WritesStatus, bool WritesStartedAt, bool WritesCompletedAt)
    {
        public bool HasProtectedWrites => WritesStatus || WritesStartedAt || WritesCompletedAt;

        public static WritingLifecycleChange Read(EntityEntry<WritingSubmission> entry) =>
            new(entry.Entity.Status, entry.Entity.StartedAt, entry.Entity.CompletedAt,
                entry.Property(s => s.Status).IsModified, entry.Property(s => s.StartedAt).IsModified,
                entry.Property(s => s.CompletedAt).IsModified);
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
