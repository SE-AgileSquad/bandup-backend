using BandUp.Domain.Identity;
using BandUp.Domain.Writing;
using BandUp.Infrastructure.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BandUp.Infrastructure.Persistence.Configurations.Writing;

internal sealed class WritingPromptConfiguration : IEntityTypeConfiguration<WritingPrompt>
{
    public void Configure(EntityTypeBuilder<WritingPrompt> builder)
    {
        builder.ToTable("writing_prompts", "writing", table => table.HasCheckConstraint(
            "writing_prompts_word_range_check", "min_words > 0 AND max_words >= min_words"));
        builder.HasKey(prompt => prompt.Id);
        builder.ToTable(table => table.HasCheckConstraint("writing_prompts_duration_positive", "duration_minutes > 0"));
        builder.Property(prompt => prompt.DurationMinutes).HasDefaultValue(WritingPrompt.DefaultDurationMinutes);
        builder.Property(prompt => prompt.Id).ValueGeneratedNever();
        builder.HasOne<BandUp.Domain.Media.MediaImage>().WithMany().HasForeignKey(prompt => prompt.PhotoId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(prompt => prompt.Title).HasMaxLength(320).IsRequired();
        builder.Property(prompt => prompt.TaskText).IsRequired();
        builder.Property(prompt => prompt.EssayType).HasMaxLength(100).IsRequired();
        builder.Property(prompt => prompt.Status).HasConversion<string>();
        builder.HasEnumCheckConstraint<WritingPrompt, WritingPromptStatus>(nameof(WritingPrompt.Status));
        builder.HasIndex(prompt => new { prompt.Status, prompt.UpdatedAt, prompt.Id });
        builder.HasOne<User>().WithMany().HasForeignKey(prompt => prompt.CreatedBy).OnDelete(DeleteBehavior.Restrict);
    }
}

