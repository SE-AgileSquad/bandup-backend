using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Domain.Billing;
using BandUp.Domain.Common;
using BandUp.Application.Features.Billing.Admin;

namespace BandUp.Application.Features.Billing;

/// <summary>
/// Opens credit accounts and keeps the ledger paired with every balance change. The signup
/// path runs inside register's transaction: it adds rows and lets the transaction behavior
/// save and commit, so a failed register never leaves an orphaned account.
/// </summary>
public sealed class CreditService(
    ICreditAccountRepository accountRepository,
    ICreditLedgerRepository ledgerRepository,
    TimeProvider timeProvider) : ICreditService
{
    public const int SignupBonus = 3;

    public async Task<Result> OpenSignupAccountAsync(Guid userId, CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();

        // Open at zero, then record the signup bonus as a transaction so the entry exists
        // in the same atomic boundary as the balance (CRD-02).
        CreditAccount account = CreditAccount.Open(userId, 0, now);
        CreditLedgerEntry entry = account.RecordTransaction(
            SignupBonus,
            CreditDelta.Grant,
            CreditLedgerEntry.SignupBonusReason,
            now);

        await accountRepository.AddAsync(account, cancellationToken);
        await ledgerRepository.AddAsync(entry, cancellationToken);

        return Result.Success();
    }

    private async Task<CreditAccount> RequireAccountAsync(Guid userId, CancellationToken cancellationToken) =>
        await accountRepository.LockAsync(userId, cancellationToken)
        ?? throw new InvalidOperationException("The expected credit account is missing.");

    public async Task LockAccountAsync(Guid userId, CancellationToken cancellationToken) =>
        await RequireAccountAsync(userId, cancellationToken);

    internal abstract class WritingAcceptance(CreditService credits)
    {
        protected Task<CreditAccount> LockAcceptanceAccountAsync(Guid owner, CancellationToken ct) =>
            credits.RequireAccountAsync(owner, ct);

        protected Task<Result> PostChargeAsync(CreditAccount lockedAccount, Guid submissionId, CancellationToken ct)
        {
            RequireSubmissionReference(submissionId);
            return credits.ApplyWritingAsync(lockedAccount, submissionId, CreditDelta.Charge, ct);
        }
    }

    /// <summary>The financial posting primitive is available only inside settlement implementations.
    /// The production handler verifies the locked submission before posting the refund.</summary>
    internal abstract class WritingSettlement(CreditService credits)
    {
        protected Task<CreditAccount> LockSettlementAccountAsync(Guid owner, CancellationToken ct) =>
            credits.RequireAccountAsync(owner, ct);

        protected Task<Result> PostRefundAsync(CreditAccount lockedAccount, Guid submissionId, CancellationToken cancellationToken)
        {
            RequireSubmissionReference(submissionId);
            return credits.ApplyWritingAsync(lockedAccount, submissionId, CreditDelta.Grant, cancellationToken);
        }
    }

    private static void RequireSubmissionReference(Guid submissionId)
    {
        if (submissionId == Guid.Empty)
        {
            throw new ArgumentException("A submission reference is required.", nameof(submissionId));
        }
    }

    private async Task<Result> ApplyWritingAsync(CreditAccount account, Guid submissionId, CreditDelta direction,
        CancellationToken cancellationToken)
    {
        var userId = account.UserId;
        bool refund = direction == CreditDelta.Grant;
        var charge = await ledgerRepository.FindAsync(CreditLedgerEntry.WritingChargeReason, submissionId, cancellationToken);
        if (charge is not null && (charge.UserId != userId || charge.Delta != -1))
        {
            return Result.Failure(Error.Conflict("The original charge does not match this operation."));
        }

        if (refund && charge is null)
        {
            return Result.Failure(Error.Conflict("A writing refund requires a matching charge."));
        }

        string reason = refund ? CreditLedgerEntry.WritingRefundReason : CreditLedgerEntry.WritingChargeReason;
        var existing = refund ? await ledgerRepository.FindAsync(reason, submissionId, cancellationToken) : charge;
        if (existing is not null)
        {
            return existing.UserId == userId && existing.Delta == (refund ? 1 : -1)
                ? Result.Success()
                : Result.Failure(Error.Conflict("The ledger reference belongs to another operation."));
        }

        if (!refund && account.Balance < 1)
        {
            return Result.Failure(new Error(Writing.WritingErrorCodes.InsufficientCredit,
                "One credit is required for this evaluation.", ErrorType.PaymentRequired));
        }

        // A replay has already returned above. Only a new refund consumes an existing reservation.
        if (refund && account.ReservedRefundCapacity == 0)
        {
            return Result.Failure(Error.Conflict("No Writing refund capacity is reserved."));
        }
        var entry = refund
            ? account.RecordWritingRefund(submissionId, timeProvider.GetUtcNow())
            : account.RecordWritingCharge(submissionId, timeProvider.GetUtcNow());
        await ledgerRepository.AddAsync(entry, cancellationToken);
        return Result.Success();
    }

    public async Task<int?> GetBalanceAsync(Guid userId, CancellationToken cancellationToken)
    {
        CreditAccount? account = await accountRepository.FindByUserIdAsync(userId, cancellationToken);

        return account?.Balance;
    }

    public async Task<Result<CreditAdjustmentResult>> AdjustAsync(Guid userId, int delta, Guid actorId,
        Guid requestId, string? note, CancellationToken cancellationToken)
    {
        if (delta is 0 or int.MinValue || actorId == Guid.Empty || requestId == Guid.Empty)
        {
            return Result<CreditAdjustmentResult>.Failure(Error.Validation(
                new Dictionary<string, string[]> { ["delta"] = ["A supported non-zero delta and valid actor/request IDs are required."] }));
        }

        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        CreditAccount? account = await accountRepository.FindByUserIdForUpdateAsync(userId, cancellationToken);
        if (account is null)
        {
            return Result<CreditAdjustmentResult>.Failure(Error.Conflict("The user has no credit account."));
        }

        CreditLedgerEntry? previous = await ledgerRepository.FindAdminAdjustmentAsync(requestId, cancellationToken);
        if (previous is not null)
        {
            if (previous.UserId != userId || previous.CreatedByAdminId != actorId
                || previous.Delta != delta || previous.Note != note)
            {
                return Result<CreditAdjustmentResult>.Failure(Error.Conflict("The request ID was already used for a different adjustment."));
            }

            return Result<CreditAdjustmentResult>.Success(new(previous.Id, userId, previous.Delta,
                previous.Note, account.Balance, previous.CreatedAt, true));
        }

        CreditLedgerEntry entry;
        try
        {
            entry = account.RecordTransaction(Math.Abs(delta), delta > 0 ? CreditDelta.Grant : CreditDelta.Charge,
                CreditLedgerEntry.AdminAdjustReason, timeProvider.GetUtcNow(), requestId, actorId, note);
        }
        catch (DomainException exception)
        {
            return Result<CreditAdjustmentResult>.Failure(Error.Conflict(exception.Message) with
            {
                ValidationErrors = new Dictionary<string, string[]> { ["delta"] = [exception.Message] },
            });
        }

        await ledgerRepository.AddAsync(entry, cancellationToken);
        return Result<CreditAdjustmentResult>.Success(new(entry.Id, userId, entry.Delta,
            entry.Note, account.Balance, entry.CreatedAt, false));
    }
}
