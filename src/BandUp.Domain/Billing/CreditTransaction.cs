using BandUp.Domain.Common;

namespace BandUp.Domain.Billing;

public sealed record CreditTransaction(
    Guid AccountId,
    decimal Amount,
    string Reason,
    DateTimeOffset OccurredAt);
