using BandUp.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BandUp.Infrastructure.Persistence.Configurations.Identity;

public sealed class OtpLoginChallengeConfiguration : IEntityTypeConfiguration<OtpLoginChallenge>
{
    public void Configure(EntityTypeBuilder<OtpLoginChallenge> builder)
    {
        builder.ToTable("otp_login_challenges", UserConfiguration.Schema, table =>
        {
            table.HasCheckConstraint("otp_login_times_check", "expires_at > created_at");
            table.HasCheckConstraint("otp_login_attempts_check",
                "max_failed_attempts BETWEEN 1 AND 100 AND failed_attempts BETWEEN 0 AND max_failed_attempts");
            table.HasCheckConstraint("otp_login_mac_check", "code_mac ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("otp_login_consumption_check",
                "consumed_at IS NULL OR (consumed_at >= created_at AND consumed_at < expires_at AND failed_attempts < max_failed_attempts)");
            table.HasCheckConstraint("otp_login_invalidation_check", "invalidated_at IS NULL OR invalidated_at >= created_at");
            table.HasCheckConstraint("otp_login_terminal_state_check", "consumed_at IS NULL OR invalidated_at IS NULL");
        });

        builder.HasKey(challenge => challenge.Id);
        builder.Property(challenge => challenge.CodeMac).HasMaxLength(64).IsRequired();
        builder.Property(challenge => challenge.CreatedAt).HasColumnType("timestamptz");
        builder.Property(challenge => challenge.ExpiresAt).HasColumnType("timestamptz");
        builder.Property(challenge => challenge.ConsumedAt).HasColumnType("timestamptz");
        builder.Property(challenge => challenge.InvalidatedAt).HasColumnType("timestamptz");
        builder.Property(challenge => challenge.Version).IsRowVersion().HasColumnName("xmin");
        builder.HasIndex(challenge => challenge.UserId).IsUnique()
            .HasFilter("consumed_at IS NULL AND invalidated_at IS NULL")
            .HasDatabaseName("ux_otp_login_open_user");
        builder.HasIndex(challenge => new { challenge.UserId, challenge.CreatedAt, challenge.Id })
            .HasDatabaseName("ix_otp_login_user_created");
        builder.HasOne<User>().WithMany().HasForeignKey(challenge => challenge.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
