using BandUp.Domain.Identity;
using BandUp.Domain.Writing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BandUp.Infrastructure.Persistence.Configurations.Writing;

internal sealed class WritingSubmissionConfiguration : IEntityTypeConfiguration<WritingSubmission>
{
    public void Configure(EntityTypeBuilder<WritingSubmission> builder)
    {
        builder.ToTable("writing_submissions", "writing", table =>
        {
            table.HasCheckConstraint("writing_submission_deadline", "queue_deadline > submitted_at");
            table.HasCheckConstraint("writing_submission_publish_attempts", "publish_attempts >= 0");
            table.HasCheckConstraint("writing_submission_lease", "(publish_lease_id IS NULL) = (publish_lease_expires_at IS NULL)");
            table.HasCheckConstraint("writing_submission_lifecycle", """
                (started_at IS NULL OR (started_at >= submitted_at AND started_at < queue_deadline))
                AND (completed_at IS NULL OR completed_at >= COALESCE(started_at, submitted_at))
                AND ((status = 'Queued' AND started_at IS NULL AND completed_at IS NULL)
                  OR (status = 'Processing' AND started_at IS NOT NULL AND completed_at IS NULL)
                  OR (status = 'Succeeded' AND started_at IS NOT NULL AND completed_at IS NOT NULL)
                  OR (status = 'Failed' AND completed_at IS NOT NULL AND (started_at IS NOT NULL OR completed_at >= queue_deadline)))
                """);
        });
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Status).HasConversion<string>();
        builder.Ignore(s => s.PromptSnapshot);
        builder.Property<string>("PromptTitle").IsRequired();
        builder.Property<string>("PromptText").IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<WritingPrompt>().WithMany().HasForeignKey(s => s.PromptId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(s => new { s.UserId, s.IdempotencyKey }).IsUnique();
        builder.HasIndex(s => new { s.UserId, s.SubmittedAt, s.Id }).IsDescending(false, true, true);
    }
}
