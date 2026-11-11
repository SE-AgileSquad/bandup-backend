using BandUp.Domain.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BandUp.Infrastructure.Persistence.Configurations.Billing;

internal sealed class CreditAccountConfiguration : IEntityTypeConfiguration<CreditAccount>
{
    public void Configure(EntityTypeBuilder<CreditAccount> builder)
    {
        builder.ToTable("credit_accounts", "billing");

        builder.HasKey(account => account.Id);

        builder.Property(account => account.UserId).IsRequired();
        builder.HasIndex(account => account.UserId).IsUnique();

        builder.Property(account => account.Balance)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(account => account.CreatedAt).IsRequired();
        builder.Property(account => account.UpdatedAt);
    }
}
