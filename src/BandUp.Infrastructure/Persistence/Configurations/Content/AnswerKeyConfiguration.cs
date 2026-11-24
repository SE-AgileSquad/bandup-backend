using BandUp.Domain.Content;
using BandUp.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BandUp.Infrastructure.Persistence.Configurations.Content;

internal sealed class AnswerKeyConfiguration : IEntityTypeConfiguration<AnswerKey>
{
    public void Configure(EntityTypeBuilder<AnswerKey> builder)
    {
        builder.ToTable("answer_keys", PassageConfiguration.Schema);
        builder.HasKey(answerKey => answerKey.Id);
        builder.Property(answerKey => answerKey.Id).ValueGeneratedNever();
        builder.Property(answerKey => answerKey.CorrectAnswer).IsRequired();

        builder.Ignore(answerKey => answerKey.IsVerified);
        builder.Ignore(answerKey => answerKey.AcceptedVariants);
        builder.PrimitiveCollection<List<string>>("_acceptedVariants")
            .HasColumnName("accepted_variants")
            .HasColumnType("text[]")
            .IsRequired();

        builder.HasOne<Question>()
            .WithOne(question => question.AnswerKey)
            .HasForeignKey<AnswerKey>(answerKey => answerKey.QuestionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(answerKey => answerKey.VerifiedBy)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
