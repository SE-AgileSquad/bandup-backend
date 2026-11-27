using BandUp.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BandUp.Infrastructure.Persistence.Configurations.Identity;

public sealed class PasswordResetTokenConfiguration : IEntityTypeConfiguration<PasswordResetToken>
{
    public void Configure(EntityTypeBuilder<PasswordResetToken> builder)
    {
        builder.ToTable("password_reset_tokens", UserConfiguration.Schema, table =>
        {
            table.HasCheckConstraint("password_reset_token_times_check", "expires_at > created_at");
            table.HasCheckConstraint("password_reset_token_hash_check", "token_hash ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("password_reset_token_consumption_check", "consumed_at IS NULL OR (consumed_at >= created_at AND consumed_at < expires_at)");
            table.HasCheckConstraint("password_reset_token_invalidation_check", "invalidated_at IS NULL OR invalidated_at >= created_at");
            table.HasCheckConstraint("password_reset_token_terminal_check", "consumed_at IS NULL OR invalidated_at IS NULL");
        });
        builder.HasKey(token => token.Id);
        builder.Property(token => token.TokenHash).HasMaxLength(64).IsRequired();
        builder.Property(token => token.CreatedAt).HasColumnType("timestamptz");
        builder.Property(token => token.ExpiresAt).HasColumnType("timestamptz");
        builder.Property(token => token.ConsumedAt).HasColumnType("timestamptz");
        builder.Property(token => token.InvalidatedAt).HasColumnType("timestamptz");
        builder.Property(token => token.Version).IsRowVersion().HasColumnName("xmin");
        builder.HasIndex(token => token.TokenHash).IsUnique();
        builder.HasIndex(token => token.ChallengeId).IsUnique();
        builder.HasIndex(token => token.UserId).IsUnique()
            .HasFilter("consumed_at IS NULL AND invalidated_at IS NULL").HasDatabaseName("ux_password_reset_token_open_user");
        builder.HasOne<User>().WithMany().HasForeignKey(token => token.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<PasswordResetChallenge>().WithMany().HasForeignKey(token => token.ChallengeId).OnDelete(DeleteBehavior.Cascade);
    }
}
