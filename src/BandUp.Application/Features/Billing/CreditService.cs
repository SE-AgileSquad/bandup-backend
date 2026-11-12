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

    public async Task<int?> GetBalanceAsync(Guid userId, CancellationToken cancellationToken)
    {
        CreditAccount? account = await accountRepository.FindByUserIdAsync(userId, cancellationToken);

        return account?.Balance;
    }
}
