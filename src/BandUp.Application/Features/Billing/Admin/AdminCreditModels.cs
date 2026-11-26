namespace BandUp.Application.Features.Billing.Admin;

public sealed record AdjustUserCreditsInput(int? Delta, Guid? RequestId, string? Note = null);
public sealed record CreditAdjustmentResult(
    Guid AdjustmentId, Guid UserId, int Delta, string? Note, int Balance, DateTimeOffset CreatedAt, bool IsReplay);
public sealed record AdminCreditBalance(Guid UserId, string DisplayName, string Email, int? Balance);
public sealed record CreditHistoryItem(
    Guid Id, int Delta, string Reason, Guid? ReferenceId, Guid? CreatedByAdminId, string? Note, DateTimeOffset CreatedAt);
