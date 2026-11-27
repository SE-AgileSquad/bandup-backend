using BandUp.Domain.Media;
using BandUp.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BandUp.Infrastructure.Persistence.Configurations.Media;

internal sealed class MediaImageConfiguration : IEntityTypeConfiguration<MediaImage>
{
    public void Configure(EntityTypeBuilder<MediaImage> builder)
    {
        builder.ToTable("images", "media", table => table.HasCheckConstraint("images_size_positive", "size_bytes > 0"));
        builder.HasKey(image => image.Id);
        builder.Property(image => image.Id).ValueGeneratedNever();
        builder.Property(image => image.StorageKey).HasMaxLength(128).IsRequired();
        builder.HasIndex(image => image.StorageKey).IsUnique();
        builder.HasIndex(image => image.CreatedAt);
        builder.Property(image => image.Format).HasMaxLength(8).IsRequired();
        builder.Property(image => image.ContentType).HasMaxLength(32).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(image => image.UploadedBy).OnDelete(DeleteBehavior.Restrict);
    }
}
