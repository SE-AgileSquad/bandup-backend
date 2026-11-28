using BandUp.Application.Common;

namespace BandUp.Application.Features.Billing;

// Financial foundation tests exercise isolated ledger postings, including malformed or pending
// records. Production callers can only request settlement of a persisted submission.
internal static class WritingRefundPostingProbe
{
    public static Task<Result> ChargeWritingAsync(this CreditService credits, Guid owner, Guid submissionId,
        CancellationToken ct) => new ChargeProbe(credits).PostAsync(owner, submissionId, ct);

    public static Task<Result> RefundWritingAsync(this CreditService credits, Guid owner, Guid submissionId,
        CancellationToken ct) => new Probe(credits).PostAsync(owner, submissionId, ct);

    private sealed class Probe(CreditService credits) : CreditService.WritingSettlement(credits)
    {
        public async Task<Result> PostAsync(Guid owner, Guid submissionId, CancellationToken ct)
        {
            var account = await LockSettlementAccountAsync(owner, ct);
            return await PostRefundAsync(account, submissionId, ct);
        }
    }

    private sealed class ChargeProbe(CreditService credits) : CreditService.WritingAcceptance(credits)
    {
        public async Task<Result> PostAsync(Guid owner, Guid submissionId, CancellationToken ct)
        {
            var account = await LockAcceptanceAccountAsync(owner, ct);
            return await PostChargeAsync(account, submissionId, ct);
        }
    }
}
