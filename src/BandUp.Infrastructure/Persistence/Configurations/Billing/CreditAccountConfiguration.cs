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

        // The account is an extension of the user: one row per user, keyed by it, so a user
        // can never accumulate two balances. The unique index below is the id in practice, and
        // the FK to identity.users is declared in the migration's companion (see the ledger).
        builder.Property(account => account.UserId).IsRequired();
        builder.HasIndex(account => account.UserId).IsUnique();

        builder.Property(account => account.Balance).IsRequired();
        builder.ToTable(t => t.HasCheckConstraint("balance_non_negative", "balance >= 0"));

        builder.Property(account => account.ReservedRefundCapacity).HasDefaultValue(0);
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("reserved_refund_capacity_non_negative", "reserved_refund_capacity >= 0");
            // Widen before adding so PostgreSQL can evaluate the check at the Int32 boundary.
            t.HasCheckConstraint("credit_account_refund_headroom", "balance::bigint + reserved_refund_capacity <= 2147483647");
        });

        builder.Property(account => account.CreatedAt).IsRequired();
        builder.Property(account => account.UpdatedAt).IsRequired();
    }
}
