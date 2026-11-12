using BandUp.Domain.Identity;
using BandUp.Domain.Learning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BandUp.Infrastructure.Persistence.Configurations.Learning;

internal sealed class LessonProgressConfiguration : IEntityTypeConfiguration<LessonProgress>
{
    public void Configure(EntityTypeBuilder<LessonProgress> builder)
    {
        builder.ToTable("lesson_progress", LessonConfiguration.Schema);
        builder.HasKey(progress => progress.Id);
        builder.Property(progress => progress.Id).ValueGeneratedNever();

        builder.HasIndex(progress => new { progress.UserId, progress.LessonId }).IsUnique();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(progress => progress.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Lesson>()
            .WithMany()
            .HasForeignKey(progress => progress.LessonId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
