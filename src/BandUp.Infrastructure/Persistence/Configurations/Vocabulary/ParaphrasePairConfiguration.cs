using BandUp.Domain.Content;
using BandUp.Domain.Identity;
using BandUp.Domain.Vocabulary;
using BandUp.Infrastructure.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BandUp.Infrastructure.Persistence.Configurations.Vocabulary;

internal sealed class ParaphrasePairConfiguration : IEntityTypeConfiguration<ParaphrasePair>
{
    internal const string Schema = "vocabulary";

    public void Configure(EntityTypeBuilder<ParaphrasePair> builder)
    {
        builder.ToTable("paraphrase_pairs", Schema);
        builder.HasKey(pair => pair.Id);
        builder.Property(pair => pair.Id).ValueGeneratedNever();

        builder.Property(pair => pair.SourceSentence).IsRequired();
        builder.Property(pair => pair.EquivalentSentence).IsRequired();
        builder.Property(pair => pair.Status).HasConversion<string>();
        builder.HasEnumCheckConstraint<ParaphrasePair, ParaphrasePairStatus>(nameof(ParaphrasePair.Status));

        builder.HasOne<Question>()
            .WithMany()
            .HasForeignKey(pair => pair.QuestionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(pair => pair.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
