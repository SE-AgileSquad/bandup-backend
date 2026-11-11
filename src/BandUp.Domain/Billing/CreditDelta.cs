namespace BandUp.Domain.Billing;

/// <summary>
/// The direction of a credit transaction, kept separate from the reason: direction is the
/// arithmetic sign of the delta, reason is why the movement happened.
/// </summary>
public enum CreditDelta
{
    /// <summary>Balance increases by the positive amount.</summary>
    Grant = 1,

    /// <summary>Balance decreases by the positive amount.</summary>
    Charge = 2,
}
