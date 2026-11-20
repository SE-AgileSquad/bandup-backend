using BandUp.Domain.Content;
using BandUp.Domain.Learning;
using BandUp.Infrastructure.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BandUp.Infrastructure.Persistence.Configurations.Learning;

internal sealed class LessonMapConfiguration : IEntityTypeConfiguration<LessonMap>
{
    public void Configure(EntityTypeBuilder<LessonMap> builder)
    {
        builder.ToTable("lesson_map", LessonConfiguration.Schema);
        builder.HasKey(map => map.Id);
        builder.Property(map => map.Id).ValueGeneratedNever();

        builder.Property(map => map.QuestionType).HasConversion<string>();
        builder.HasEnumCheckConstraint<LessonMap, QuestionType>(nameof(LessonMap.QuestionType));

        // A null error pattern is the fallback lesson for the question type.
        builder.HasIndex(map => new { map.QuestionType, map.ErrorPattern })
            .IsUnique()
            .AreNullsDistinct(false);

        builder.HasOne<Lesson>()
            .WithMany()
            .HasForeignKey(map => map.LessonId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
