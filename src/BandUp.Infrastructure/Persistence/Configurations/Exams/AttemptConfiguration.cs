using BandUp.Domain.Content;
using BandUp.Domain.Exams;
using BandUp.Domain.Identity;
using BandUp.Infrastructure.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BandUp.Infrastructure.Persistence.Configurations.Exams;

internal sealed class AttemptConfiguration : IEntityTypeConfiguration<Attempt>
{
    internal const string Schema = "exams";

    public void Configure(EntityTypeBuilder<Attempt> builder)
    {
        builder.ToTable("attempts", Schema);
        builder.HasKey(attempt => attempt.Id);
        builder.Property(attempt => attempt.Id).ValueGeneratedNever();

        builder.Property(attempt => attempt.Status).HasConversion<string>();
        builder.HasEnumCheckConstraint<Attempt, AttemptStatus>(nameof(Attempt.Status));

        builder.Property(attempt => attempt.StartedAt).HasColumnType("timestamptz");
        builder.Property(attempt => attempt.ExpiresAt).HasColumnType("timestamptz");
        builder.Property(attempt => attempt.SubmittedAt).HasColumnType("timestamptz");
        builder.Property(attempt => attempt.IdempotencyKey).HasMaxLength(100);

        // One digit after the point (5.5), at most 9.0. Null until the attempt is graded.
        builder.Property(attempt => attempt.ApproxBand).HasPrecision(2, 1);
        builder.ToTable(table => table.HasCheckConstraint(
            "attempts_approx_band_check",
            "approx_band IS NULL OR (approx_band >= 0 AND approx_band <= 9)"));

        // At most one attempt in progress per learner and passage. The handler serializes starts
        // with a lock; this index is the backstop that makes a second one impossible.
        builder.HasIndex(attempt => new { attempt.UserId, attempt.PassageId })
            .IsUnique()
            .HasFilter("status = 'InProgress'")
            .HasDatabaseName("ux_attempts_user_passage_in_progress");

        // A submission key is unique per learner, so a retry can be recognised.
        builder.HasIndex(attempt => new { attempt.UserId, attempt.IdempotencyKey })
            .IsUnique()
            .HasFilter("idempotency_key IS NOT NULL")
            .HasDatabaseName("ux_attempts_user_idempotency_key");

        builder.HasIndex(attempt => new { attempt.UserId, attempt.StartedAt })
            .HasDatabaseName("ix_attempts_user_started_at");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(attempt => attempt.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Passage>()
            .WithMany()
            .HasForeignKey(attempt => attempt.PassageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(attempt => attempt.Answers)
            .WithOne()
            .HasForeignKey(answer => answer.AttemptId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(attempt => attempt.Answers)
            .HasField("_answers")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        // No xmin token on purpose: every change to an attempt happens under a row lock, so
        // concurrent writers queue instead of failing at commit.
    }
}
