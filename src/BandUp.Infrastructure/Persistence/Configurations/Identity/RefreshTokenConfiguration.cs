using BandUp.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BandUp.Infrastructure.Persistence.Configurations.Identity;

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens", UserConfiguration.Schema);

        builder.HasKey(token => token.Id);

        builder.Property(token => token.UserId)
            .IsRequired();

        builder.Property(token => token.TokenHash)
            .HasMaxLength(64)
            .IsRequired();

        // The raw token is never stored, so a leak of this table is not a leak of sessions.
        builder.HasIndex(token => token.TokenHash)
            .IsUnique()
            .HasDatabaseName("ux_refresh_tokens_token_hash");

        // Rotation looks up a user's live sessions; the common case is a handful of rows.
        builder.HasIndex(token => new { token.UserId, token.ExpiresAt })
            .HasDatabaseName("ix_refresh_tokens_user_expires");

        builder.Property(token => token.CreatedAt)
            .HasColumnType("timestamptz");

        builder.Property(token => token.ExpiresAt)
            .HasColumnType("timestamptz");

        builder.Property(token => token.RevokedAt)
            .HasColumnType("timestamptz");

        builder.Property(token => token.UserAgent)
            .HasMaxLength(200);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}