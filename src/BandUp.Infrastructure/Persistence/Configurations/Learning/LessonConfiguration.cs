using BandUp.Domain.Content;
using BandUp.Domain.Learning;
using BandUp.Infrastructure.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BandUp.Infrastructure.Persistence.Configurations.Learning;

internal sealed class LessonConfiguration : IEntityTypeConfiguration<Lesson>
{
    internal const string Schema = "learning";

    public void Configure(EntityTypeBuilder<Lesson> builder)
    {
        builder.ToTable("lessons", Schema);
        builder.HasKey(lesson => lesson.Id);
        builder.Property(lesson => lesson.Id).ValueGeneratedNever();

        builder.Property(lesson => lesson.Title).IsRequired();
        builder.Property(lesson => lesson.Body).IsRequired();
        builder.Property(lesson => lesson.QuestionType).HasConversion<string>();
        builder.Property(lesson => lesson.Status).HasConversion<string>();
        builder.HasEnumCheckConstraint<Lesson, QuestionType>(nameof(Lesson.QuestionType));
        builder.HasEnumCheckConstraint<Lesson, LessonStatus>(nameof(Lesson.Status));

        builder.HasMany(lesson => lesson.Examples)
            .WithOne()
            .HasForeignKey(example => example.LessonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(lesson => lesson.Examples)
            .HasField("_examples")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
