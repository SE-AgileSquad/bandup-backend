using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Billing;
using BandUp.Domain.Billing;
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

    private sealed class TestCreditAccountRepository : ICreditAccountRepository
    {
        public CreditAccount? AddedAccount { get; private set; }
        public CreditAccount? Stored { get; set; }

        public Task AddAsync(CreditAccount account, CancellationToken ct)
        {
            AddedAccount = account;
            return Task.CompletedTask;
        }

        public Task<CreditAccount?> FindByUserIdAsync(Guid userId, CancellationToken ct) =>
            Task.FromResult(Stored is { } account && account.UserId == userId ? account : null);
    }

    private sealed class TestLedgerRepository : ICreditLedgerRepository
    {
        public CreditLedgerEntry? AddedEntry { get; private set; }
        public Task AddAsync(CreditLedgerEntry entry, CancellationToken ct)
        {
            AddedEntry = entry;
            return Task.CompletedTask;
        }
    }
}
