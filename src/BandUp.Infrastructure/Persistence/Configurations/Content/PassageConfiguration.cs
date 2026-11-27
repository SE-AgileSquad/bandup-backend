using BandUp.Domain.Content;
using BandUp.Domain.Identity;
using BandUp.Infrastructure.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BandUp.Infrastructure.Persistence.Configurations.Content;

internal sealed class PassageConfiguration : IEntityTypeConfiguration<Passage>
{
    internal const string Schema = "content";

    public void Configure(EntityTypeBuilder<Passage> builder)
    {
        builder.ToTable("passages", Schema);
        builder.ToTable(table => table.HasCheckConstraint("passages_duration_positive", "duration_minutes > 0"));
        builder.Property(passage => passage.DurationMinutes).HasDefaultValue(Passage.DefaultDurationMinutes);
        builder.HasKey(passage => passage.Id);
        builder.Property(passage => passage.Id).ValueGeneratedNever();
        builder.HasOne<BandUp.Domain.Media.MediaImage>().WithMany().HasForeignKey(passage => passage.PhotoId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(passage => passage.Title).IsRequired();
        builder.Property(passage => passage.Body).IsRequired();
        builder.Property(passage => passage.SourceNote).IsRequired();
        builder.Property(passage => passage.UsageBasis).IsRequired();
        builder.Property(passage => passage.UpdatedAt).HasColumnType("timestamptz");
        builder.Property(passage => passage.Status).HasConversion<string>();
        builder.HasEnumCheckConstraint<Passage, PassageStatus>(nameof(Passage.Status));

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(passage => passage.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Passage>()
            .WithMany()
            .HasForeignKey(passage => passage.PreviousVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(passage => passage.Questions)
            .WithOne()
            .HasForeignKey(question => question.PassageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(passage => passage.Questions)
            .HasField("_questions")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
