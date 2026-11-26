using BandUp.Application.Common;

namespace BandUp.Application.Abstractions;

/// <summary>
/// The only way a balance changes. Every operation writes the ledger entry and updates the
/// account in one transaction, so the sum invariant (CRD-02) is enforced by construction.
/// Writing operations require one caller-owned Read Committed transaction. On cancellation,
/// persistence failure or manual rollback to a savepoint, abandon the transaction and dispose
/// the scope. Retry with a fresh context. This also applies to direct EF transaction owners.
/// </summary>
public interface ICreditService
{
    /// <summary>
    /// Opens the account every new user gets: balance from <c>Credits:SignupBonus</c> and the
    /// matching <c>SignupBonus</c> ledger line (AUTH-01, CRD-01). Called from register's
    /// transaction, so no separate save happens here.
    /// </summary>
    Task<Result> OpenSignupAccountAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Hold the account row lock until the caller completes its transaction.</summary>
    Task LockAccountAsync(Guid userId, CancellationToken cancellationToken);

    Task<Result> ChargeWritingAsync(Guid userId, Guid submissionId, CancellationToken cancellationToken);
    Task<Result> RefundWritingAsync(Guid userId, Guid submissionId, CancellationToken cancellationToken);

    /// <summary>The current balance, or null while no account exists yet.</summary>
    Task<int?> GetBalanceAsync(Guid userId, CancellationToken cancellationToken);
}
