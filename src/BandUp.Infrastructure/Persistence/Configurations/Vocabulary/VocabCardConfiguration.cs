using BandUp.Domain.Content;
using BandUp.Domain.Identity;
using BandUp.Domain.Vocabulary;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BandUp.Infrastructure.Persistence.Configurations.Vocabulary;

internal sealed class VocabCardConfiguration : IEntityTypeConfiguration<VocabCard>
{
    public void Configure(EntityTypeBuilder<VocabCard> builder)
    {
        builder.ToTable("vocab_cards", ParaphrasePairConfiguration.Schema);
        builder.HasKey(card => card.Id);
        builder.Property(card => card.Id).ValueGeneratedNever();

        builder.Property(card => card.Word).IsRequired();
        builder.Property(card => card.ContextSentence).IsRequired();
        builder.HasIndex(card => new { card.UserId, card.Word }).IsUnique();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(card => card.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Question>()
            .WithMany()
            .HasForeignKey(card => card.SourceQuestionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
