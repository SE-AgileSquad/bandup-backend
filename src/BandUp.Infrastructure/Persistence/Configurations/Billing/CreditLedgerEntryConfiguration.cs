using BandUp.Domain.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BandUp.Infrastructure.Persistence.Configurations.Billing;

internal sealed class CreditLedgerEntryConfiguration : IEntityTypeConfiguration<CreditLedgerEntry>
{
    public void Configure(EntityTypeBuilder<CreditLedgerEntry> builder)
    {
        builder.ToTable("credit_ledger", "billing");

        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.UserId).IsRequired();
        builder.HasIndex(entry => entry.UserId);

        builder.Property(entry => entry.Delta).IsRequired();
        builder.ToTable(t => t.HasCheckConstraint("delta_non_zero", "delta <> 0"));

        builder.Property(entry => entry.Reason)
            .HasMaxLength(20)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(entry => entry.ReferenceId);
        builder.Property(entry => entry.CreatedByAdminId);
        builder.Property(entry => entry.Note).HasMaxLength(200);

        builder.Property(entry => entry.CreatedAt).IsRequired();

        // One charge and at most one refund per submission (§6). The partial index is declared
        // in the migration because EF has no first-class partial-index API; it is the same
        // guarantee either way.
        builder.HasIndex(entry => new { entry.Reason, entry.ReferenceId })
            .IsUnique()
            .HasFilter("reference_id IS NOT NULL")
            .HasDatabaseName("ux_ledger_reason_reference");
    }
}
