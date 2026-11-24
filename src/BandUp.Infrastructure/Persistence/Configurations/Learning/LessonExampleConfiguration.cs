using BandUp.Domain.Learning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BandUp.Infrastructure.Persistence.Configurations.Learning;

internal sealed class LessonExampleConfiguration : IEntityTypeConfiguration<LessonExample>
{
    public void Configure(EntityTypeBuilder<LessonExample> builder)
    {
        builder.ToTable("lesson_examples", LessonConfiguration.Schema);
        builder.HasKey(example => example.Id);
        builder.Property(example => example.Id).ValueGeneratedNever();

        builder.Property(example => example.EvidenceSentence).IsRequired();
        builder.Property(example => example.Paraphrase).IsRequired();
    }
}
