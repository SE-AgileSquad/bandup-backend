using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Domain.Billing;

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

    public Task<Result> OpenSignupAccountAsync(Guid userId, CancellationToken cancellationToken)
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

        accountRepository.AddAsync(account, cancellationToken);
        ledgerRepository.AddAsync(entry, cancellationToken);

        return Task.FromResult(Result.Success());
    }

    private async Task<CreditAccount> RequireAccountAsync(Guid userId, CancellationToken cancellationToken) =>
        await accountRepository.LockAsync(userId, cancellationToken)
        ?? throw new InvalidOperationException("The expected credit account is missing.");

    public async Task LockAccountAsync(Guid userId, CancellationToken cancellationToken) =>
        await RequireAccountAsync(userId, cancellationToken);

    public Task<Result> ChargeWritingAsync(Guid userId, Guid submissionId, CancellationToken cancellationToken) =>
        ApplyWritingAsync(userId, submissionId, CreditDelta.Charge, cancellationToken);

    public Task<Result> RefundWritingAsync(Guid userId, Guid submissionId, CancellationToken cancellationToken) =>
        ApplyWritingAsync(userId, submissionId, CreditDelta.Grant, cancellationToken);

    private async Task<Result> ApplyWritingAsync(Guid userId, Guid submissionId, CreditDelta direction,
        CancellationToken cancellationToken)
    {
        if (submissionId == Guid.Empty)
        {
            throw new ArgumentException("A submission reference is required.", nameof(submissionId));
        }

        CreditAccount account = await RequireAccountAsync(userId, cancellationToken);
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

        var entry = account.RecordTransaction(1, direction, reason, timeProvider.GetUtcNow(), submissionId);
        await ledgerRepository.AddAsync(entry, cancellationToken);
        return Result.Success();
    }

    public async Task<int?> GetBalanceAsync(Guid userId, CancellationToken cancellationToken)
    {
        CreditAccount? account = await accountRepository.FindByUserIdAsync(userId, cancellationToken);

        return account?.Balance;
    }
}
