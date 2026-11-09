using BandUp.Domain.Identity;
using BandUp.Infrastructure.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BandUp.Infrastructure.Persistence.Configurations.Identity;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public const string Schema = "identity";

    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users", Schema);

        builder.HasKey(user => user.Id);

        // citext gives case-insensitive comparison and a case-insensitive unique index, so
        // "A@x.com" and "a@x.com" cannot both register (AUTH-02).
        builder.Property(user => user.Email)
            .HasColumnType("citext")
            .HasMaxLength(320)
            .IsRequired();

        builder.HasIndex(user => user.Email)
            .IsUnique()
            .HasDatabaseName("ux_users_email");

        builder.Property(user => user.DisplayName)
            .HasMaxLength(60)
            .IsRequired();

        builder.Property(user => user.PasswordHash)
            .IsRequired();

        builder.Property(user => user.Role)
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(Role.User);

        builder.Property(user => user.IsActive)
            .HasDefaultValue(true);

        builder.Property(user => user.CreatedAt)
            .HasColumnType("timestamptz");

        builder.Property(user => user.LastLoginAt)
            .HasColumnType("timestamptz");

        // Declared after ToTable so the constraint name can be built from the table name.
        builder.HasEnumCheckConstraint<User, Role>(nameof(User.Role));
    }
}