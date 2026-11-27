using BandUp.Domain.Common;

namespace BandUp.Domain.Billing;

/// <summary>
/// A user's credit balance. The invariants: one account per user, the balance never goes
/// below zero, and every change is mirrored by a ledger entry (CRD-02).
/// </summary>
public sealed class CreditAccount : Entity
{
    private CreditAccount() { }

    private CreditAccount(Guid id, Guid userId, int balance, DateTimeOffset createdAt)
        : base(id)
    {
        UserId = userId;
        Balance = balance;
        CreatedAt = createdAt;
    }

    public Guid UserId { get; private set; }

    public int Balance { get; private set; }

    /// <summary>Balance headroom held for outstanding Writing refunds.</summary>
    public int ReservedRefundCapacity { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static CreditAccount Open(Guid userId, int initialBalance, DateTimeOffset now)
    {
        if (initialBalance < 0)
        {
            throw new DomainException("The initial credit balance cannot be negative.");
        }

        return new CreditAccount(Guid.CreateVersion7(), userId, initialBalance, now);
    }

    /// <summary>
    /// Executes a business transaction on this account: validates the direction and balance,
    /// mutates the account, and returns the ledger entry the aggregate mints. The entry is
    /// produced here so the balance and its history cannot be written apart (CRD-02).
    /// </summary>
    public CreditLedgerEntry RecordTransaction(
        int amount,
        CreditDelta deltaType,
        string reason,
        DateTimeOffset now,
        Guid? referenceId = null,
        Guid? createdByAdminId = null,
        string? note = null)
    {
        if (amount <= 0)
        {
            throw new DomainException("A credit transaction must carry a positive amount.");
        }

        if (!Enum.IsDefined(deltaType))
        {
            throw new DomainException("The credit transaction direction is invalid.");
        }

        int signedDelta = deltaType == CreditDelta.Grant ? amount : -amount;
        long candidate = (long)Balance + signedDelta;
        long reserved = ReservedRefundCapacity;
        if (reason is CreditLedgerEntry.WritingChargeReason or CreditLedgerEntry.WritingRefundReason)
        {
            int expectedDelta = reason == CreditLedgerEntry.WritingChargeReason ? -1 : 1;
            if (signedDelta != expectedDelta || referenceId is null || referenceId == Guid.Empty)
            {
                throw new DomainException("Writing movements require one credit and a submission reference.");
            }
            // A charge exchanges balance for refund headroom; its refund reverses that exchange.
            reserved -= signedDelta;
        }

        if (candidate < 0)
        {
            throw new DomainException(
                $"A {reason} of {signedDelta} would leave a negative balance.");
        }

        if (reserved < 0)
        {
            throw new DomainException("No Writing refund capacity is reserved.");
        }
        if (candidate + reserved > int.MaxValue)
        {
            throw new DomainException("The resulting credit balance exceeds the supported range.");
        }

        var entry = CreditLedgerEntry.Record(
            UserId,
            signedDelta,
            reason,
            now,
            referenceId,
            createdByAdminId,
            note);
        Balance = (int)candidate;
        ReservedRefundCapacity = (int)reserved;
        UpdatedAt = now;
        return entry;
    }

    /// <summary>For successful settlement only. The caller must hold the account and submission locks
    /// and verify the owner's outstanding obligation before releasing it in the same transaction.
    /// The account-level count cannot establish which submission is unsettled.</summary>
    internal void ReleaseWritingRefundCapacity(DateTimeOffset now)
    {
        if (ReservedRefundCapacity <= 0)
        {
            throw new DomainException("No Writing refund capacity is reserved.");
        }
        ReservedRefundCapacity--;
        UpdatedAt = now;
    }
}
