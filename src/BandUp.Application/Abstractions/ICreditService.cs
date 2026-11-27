using BandUp.Application.Common;
using BandUp.Application.Features.Billing.Admin;

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
    /// Opens the account every new user gets with the signup grant and the
    /// matching <c>SignupBonus</c> ledger line (AUTH-01, CRD-01). Called from register's
    /// transaction, so no separate save happens here.
    /// </summary>
    Task<Result> OpenSignupAccountAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Hold the account row lock until the caller completes its transaction.</summary>
    Task LockAccountAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Debit once per submission. Acceptance locks the account before the prompt.</summary>
    Task<Result> ChargeWritingAsync(Guid userId, Guid submissionId, CancellationToken cancellationToken);
    /// <summary>Reverse the owner's matching charge once. Failure handling locks the account before the submission.</summary>
    Task<Result> RefundWritingAsync(Guid userId, Guid submissionId, CancellationToken cancellationToken);

    /// <summary>The current balance, or null while no account exists yet.</summary>
    Task<int?> GetBalanceAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Called within an admin command transaction, after the current super-admin role is checked under its lock.</summary>
    Task<Result<CreditAdjustmentResult>> AdjustAsync(Guid userId, int delta, Guid actorId,
        Guid requestId, string? note, CancellationToken cancellationToken);
}
