using BandUp.Application.Common;

namespace BandUp.Application.Abstractions;

/// <summary>
/// The only way a balance changes. Every operation writes the ledger entry and updates the
/// account in one transaction, so the sum invariant (CRD-02) is enforced by construction.
/// </summary>
public interface ICreditService
{
    /// <summary>
    /// Opens the account every new user gets: balance from <c>Credits:SignupBonus</c> and the
    /// matching <c>SignupBonus</c> ledger line (AUTH-01, CRD-01). Called from register's
    /// transaction, so no separate save happens here.
    /// </summary>
    Task<Result> OpenSignupAccountAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>The current balance, or null while no account exists yet.</summary>
    Task<int?> GetBalanceAsync(Guid userId, CancellationToken cancellationToken);
}
