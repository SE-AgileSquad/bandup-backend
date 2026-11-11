using BandUp.Domain.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BandUp.Infrastructure.Persistence.Configurations.Billing;

internal sealed class CreditTransactionConfiguration : IEntityTypeConfiguration<CreditTransaction>
{
    public void Configure(EntityTypeBuilder<CreditTransaction> builder)
    {
        // ValueObject, not an entity
        builder.HasNoKey();
        builder.ToTable("credit_transactions", "billing");

        builder.Property(t => t.AccountId).IsRequired();
        builder.Property(t => t.Amount).HasPrecision(18, 2).IsRequired();
        builder.Property(t => t.Reason).HasMaxLength(200).IsRequired();
        builder.Property(t => t.OccurredAt).IsRequired();
    }
}
