using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Billing;
using BandUp.Domain.Billing;
using BandUp.Application.Features.Billing.Admin;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace BandUp.UnitTests.Features.Billing;

/// <summary>
/// CRD-01 and the shape AUTH-01 depends on: opening the signup account writes the balance
/// and its ledger line together, so the sum invariant starts true.
/// </summary>
public sealed class CreditServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _time = new(Now);
    private readonly TestCreditAccountRepository _accounts = new();
    private readonly TestLedgerRepository _ledger = new();

    [Fact]
    public async Task OpenSignupAccount_Writes_The_Balance_And_The_Ledger_Line_Together()
    {
        CreditService service = new(_accounts, _ledger, _time);
        Guid userId = Guid.CreateVersion7();

        Result result = await service.OpenSignupAccountAsync(userId, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        _accounts.AddedAccount.ShouldNotBeNull();
        _accounts.AddedAccount!.UserId.ShouldBe(userId);
        _accounts.AddedAccount.Balance.ShouldBe(CreditService.SignupBonus);
        _accounts.AddedAccount.CreatedAt.ShouldBe(Now);

        _ledger.AddedEntry.ShouldNotBeNull();
        _ledger.AddedEntry!.UserId.ShouldBe(userId);
        _ledger.AddedEntry.Delta.ShouldBe(CreditService.SignupBonus);
        _ledger.AddedEntry.Reason.ShouldBe(CreditLedgerEntry.SignupBonusReason);
        _ledger.AddedEntry.CreatedAt.ShouldBe(Now);
    }

    [Fact]
    public async Task GetBalance_Throws_Nothing_When_No_Account_Exists()
    {
        CreditService service = new(_accounts, _ledger, _time);

        int? balance = await service.GetBalanceAsync(Guid.CreateVersion7(), CancellationToken.None);

        balance.ShouldBeNull();
    }

    [Fact]
    public async Task GetBalance_Returns_The_Stored_Balance()
    {
        Guid userId = Guid.CreateVersion7();
        _accounts.Stored = CreditAccount.Open(userId, 7, Now);
        CreditService service = new(_accounts, _ledger, _time);

        int? balance = await service.GetBalanceAsync(userId, CancellationToken.None);

        balance.ShouldBe(7);
    }

    [Fact]
    public async Task WritingOperationsRequireAnAccountAndMatchingCharge()
    {
        CreditService service = new(_accounts, _ledger, _time);
        Guid owner = Guid.NewGuid();
        Guid reference = Guid.NewGuid();
        var ct = CancellationToken.None;
        await Should.ThrowAsync<InvalidOperationException>(() => service.ChargeWritingAsync(owner, reference, ct));
        _accounts.Stored = CreditAccount.Open(owner, 1, Now);
        (await service.RefundWritingAsync(owner, reference, ct)).IsFailure.ShouldBeTrue();
        (await service.ChargeWritingAsync(owner, reference, ct)).IsSuccess.ShouldBeTrue();
        (await service.ChargeWritingAsync(owner, reference, ct)).IsSuccess.ShouldBeTrue();
        (await service.ChargeWritingAsync(owner, Guid.NewGuid(), ct)).Error!.Type.ShouldBe(ErrorType.PaymentRequired);
        _accounts.Stored.Balance.ShouldBe(0);
        (await service.RefundWritingAsync(owner, reference, ct)).IsSuccess.ShouldBeTrue();
        (await service.RefundWritingAsync(owner, reference, ct)).IsSuccess.ShouldBeTrue();
        _accounts.Stored.Balance.ShouldBe(1);
        _accounts.Stored = CreditAccount.Open(Guid.NewGuid(), 0, Now);
        (await service.RefundWritingAsync(_accounts.Stored.UserId, reference, ct)).IsFailure.ShouldBeTrue();
        _accounts.Stored.Balance.ShouldBe(0);
    }

    [Fact]
    public async Task ReservationsTrackOnlyNewMovementsAcrossMultipleReferences()
    {
        var owner = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var account = CreditAccount.Open(owner, 2, Now);
        _accounts.Stored = account;
        CreditService service = new(_accounts, _ledger, _time);
        var ct = CancellationToken.None;
        (await service.ChargeWritingAsync(owner, first, ct)).IsSuccess.ShouldBeTrue();
        (await service.ChargeWritingAsync(owner, second, ct)).IsSuccess.ShouldBeTrue();
        (await service.ChargeWritingAsync(owner, second, ct)).IsSuccess.ShouldBeTrue();
        (await service.ChargeWritingAsync(owner, Guid.NewGuid(), ct)).Error!.Type.ShouldBe(ErrorType.PaymentRequired);
        (await service.RefundWritingAsync(owner, Guid.NewGuid(), ct)).IsFailure.ShouldBeTrue();
        account.ReservedRefundCapacity.ShouldBe(2);
        account.Balance.ShouldBe(0);
        (await service.RefundWritingAsync(owner, first, ct)).IsSuccess.ShouldBeTrue();
        (await service.RefundWritingAsync(owner, first, ct)).IsSuccess.ShouldBeTrue();
        account.ReservedRefundCapacity.ShouldBe(1);
        account.Balance.ShouldBe(1);

        // Matching ledger evidence alone cannot fund a refund on an account with no headroom reserved.
        var inconsistent = CreditAccount.Open(owner, 1, Now);
        _accounts.Stored = inconsistent;
        (await service.RefundWritingAsync(owner, second, ct)).Error!.Type.ShouldBe(ErrorType.Conflict);
        inconsistent.ReservedRefundCapacity.ShouldBe(0);
        inconsistent.Balance.ShouldBe(1);
    }

    private sealed class TestCreditAccountRepository : ICreditAccountRepository
    {
        public CreditAccount? AddedAccount { get; private set; }
        public CreditAccount? Stored { get; set; }

        public Task AddAsync(CreditAccount account, CancellationToken ct)
        {
            AddedAccount = account;
            return Task.CompletedTask;
        }

        public Task<CreditAccount?> LockAsync(Guid userId, CancellationToken ct) => FindByUserIdAsync(userId, ct);

        public Task<CreditAccount?> FindByUserIdAsync(Guid userId, CancellationToken ct) =>
            Task.FromResult(Stored is { } account && account.UserId == userId ? account : null);

        public Task<CreditAccount?> FindByUserIdForUpdateAsync(Guid userId, CancellationToken ct) => FindByUserIdAsync(userId, ct);
    }

    private sealed class TestLedgerRepository : ICreditLedgerRepository
    {
        private readonly List<CreditLedgerEntry> _entries = [];
        public Task<CreditLedgerEntry?> FindAsync(string reason, Guid referenceId, CancellationToken ct) =>
            Task.FromResult(_entries.SingleOrDefault(entry => entry.Reason == reason && entry.ReferenceId == referenceId));

        public CreditLedgerEntry? AddedEntry { get; private set; }
        public Task AddAsync(CreditLedgerEntry entry, CancellationToken ct)
        {
            _entries.Add(entry);
            AddedEntry = entry;
            return Task.CompletedTask;
        }

        public Task<CreditLedgerEntry?> FindAdminAdjustmentAsync(Guid requestId, CancellationToken ct) => Task.FromResult<CreditLedgerEntry?>(null);

        public Task<PagedResult<CreditHistoryItem>> ListForUserAsync(Guid userId, PageRequest page, CancellationToken ct) =>
            Task.FromResult(PagedResult<CreditHistoryItem>.Create([], page, 0));
    }
}
