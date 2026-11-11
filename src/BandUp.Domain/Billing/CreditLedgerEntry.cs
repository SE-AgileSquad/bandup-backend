using BandUp.Domain.Common;

namespace BandUp.Domain.Billing;

/// <summary>
/// One immutable line in the credit ledger. The balance is a running total of these deltas;
/// the pair is written in one transaction, so the sum invariant (CRD-02) always holds.
/// </summary>
public sealed class CreditLedgerEntry : Entity
{
    /// <summary>Reason codes the product defines. New reasons arrive with their feature.</summary>
    public const string SignupBonusReason = "SignupBonus";
    public const string WritingChargeReason = "WritingCharge";
    public const string WritingRefundReason = "WritingRefund";
    public const string AdminGrantReason = "AdminGrant";
    public const string AdminAdjustReason = "AdminAdjust";

    private CreditLedgerEntry() { }

    private CreditLedgerEntry(
        Guid id,
        Guid userId,
        int delta,
        string reason,
        Guid? referenceId,
        Guid? createdByAdminId,
        string? note,
        DateTimeOffset createdAt)
        : base(id)
    {
        UserId = userId;
        Delta = delta;
        Reason = reason;
        ReferenceId = referenceId;
        CreatedByAdminId = createdByAdminId;
        Note = note;
        CreatedAt = createdAt;
    }

    public Guid UserId { get; private set; }

    /// <summary>Non-zero; positive is a grant, negative a charge.</summary>
    public int Delta { get; private set; }

    public string Reason { get; private set; } = null!;

    /// <summary>The submission id for a charge or refund; null otherwise.</summary>
    public Guid? ReferenceId { get; private set; }

    public Guid? CreatedByAdminId { get; private set; }

    public string? Note { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    // Only the aggregate root may mint a ledger line; the entry's identity is the account's.
    internal static CreditLedgerEntry Record(
        Guid userId,
        int delta,
        string reason,
        DateTimeOffset createdAt,
        Guid? referenceId = null,
        Guid? createdByAdminId = null,
        string? note = null)
    {
        if (delta == 0)
        {
            throw new DomainException("A ledger entry must have a non-zero delta.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("A ledger entry must carry a reason.");
        }

        return new CreditLedgerEntry(
            Guid.CreateVersion7(),
            userId,
            delta,
            reason,
            referenceId,
            createdByAdminId,
            note,
            createdAt);
    }
}
