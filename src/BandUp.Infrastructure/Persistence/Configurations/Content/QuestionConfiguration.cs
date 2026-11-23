using BandUp.Domain.Content;
using BandUp.Infrastructure.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BandUp.Infrastructure.Persistence.Configurations.Content;

internal sealed class QuestionConfiguration : IEntityTypeConfiguration<Question>
{
    public void Configure(EntityTypeBuilder<Question> builder)
    {
        builder.ToTable("questions", PassageConfiguration.Schema);
        builder.HasKey(question => question.Id);
        builder.Property(question => question.Id).ValueGeneratedNever();

        builder.Property(question => question.Prompt).IsRequired();
        builder.Property(question => question.Type).HasConversion<string>();
        builder.Property(question => question.Status).HasConversion<string>();

        builder.HasEnumCheckConstraint<Question, QuestionType>(nameof(Question.Type));
        builder.HasEnumCheckConstraint<Question, QuestionStatus>(nameof(Question.Status));
    }
}
