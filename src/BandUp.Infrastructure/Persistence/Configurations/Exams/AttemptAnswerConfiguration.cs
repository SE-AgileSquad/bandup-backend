using BandUp.Domain.Content;
using BandUp.Domain.Exams;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BandUp.Infrastructure.Persistence.Configurations.Exams;

internal sealed class AttemptAnswerConfiguration : IEntityTypeConfiguration<AttemptAnswer>
{
    public void Configure(EntityTypeBuilder<AttemptAnswer> builder)
    {
        builder.ToTable("attempt_answers", AttemptConfiguration.Schema);
        builder.HasKey(answer => answer.Id);
        builder.Property(answer => answer.Id).ValueGeneratedNever();

        builder.Property(answer => answer.SelectedAnswer)
            .HasMaxLength(Attempt.MaxAnswerLength)
            .IsRequired();

        builder.HasIndex(answer => new { answer.AttemptId, answer.QuestionId })
            .IsUnique()
            .HasDatabaseName("ux_attempt_answers_attempt_question");

        builder.HasOne<Question>()
            .WithMany()
            .HasForeignKey(answer => answer.QuestionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
