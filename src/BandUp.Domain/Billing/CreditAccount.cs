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

        if (candidate < 0)
        {
            throw new DomainException(
                $"A {reason} of {signedDelta} would leave a negative balance.");
        }

        if (candidate > int.MaxValue)
        {
            throw new DomainException("The resulting credit balance exceeds the supported range.");
        }

        Balance = (int)candidate;
        UpdatedAt = now;

        return CreditLedgerEntry.Record(
            UserId,
            signedDelta,
            reason,
            now,
            referenceId,
            createdByAdminId,
            note);
    }
}
