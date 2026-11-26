using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Billing;
using BandUp.Application.Features.Billing.Admin;
using BandUp.Application.Features.Identity;
using BandUp.Domain.Billing;
using BandUp.Domain.Common;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Shouldly;

namespace BandUp.UnitTests.Features.Billing;

public sealed class AdminCreditTests
{
    [Theory]
    [InlineData(17, 20)]
    [InlineData(-3, 0)]
    public async Task Adjustment_Uses_The_Existing_Aggregate_And_Records_Actor_Note_And_Request(int delta, int expected)
    {
        Guid userId = Guid.NewGuid();
        Guid actorId = Guid.NewGuid();
        Guid requestId = Guid.NewGuid();
        FakeTimeProvider clock = new();
        CreditAccount account = CreditAccount.Open(userId, 3, clock.GetUtcNow());
        ICreditAccountRepository accounts = Substitute.For<ICreditAccountRepository>();
        accounts.FindByUserIdForUpdateAsync(userId, Arg.Any<CancellationToken>()).Returns(account);
        ICreditLedgerRepository ledger = Substitute.For<ICreditLedgerRepository>();
        CreditService service = new(accounts, ledger, clock);

        var result = await service.AdjustAsync(userId, delta, actorId, requestId, " Practice ", CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Balance.ShouldBe(expected);
        account.Balance.ShouldBe(expected);
        result.Value.Note.ShouldBe("Practice");
        await ledger.Received(1).AddAsync(Arg.Is<CreditLedgerEntry>(entry => entry.Delta == delta
            && entry.ReferenceId == requestId && entry.CreatedByAdminId == actorId
            && entry.Reason == CreditLedgerEntry.AdminAdjustReason && entry.Note == "Practice"), CancellationToken.None);
    }

    [Theory]
    [InlineData(3, -4)]
    [InlineData(int.MaxValue, 1)]
    public async Task Invalid_Balances_Do_Not_Change_The_Account_Or_Write_History(int balance, int delta)
    {
        Guid userId = Guid.NewGuid();
        CreditAccount account = CreditAccount.Open(userId, balance, DateTimeOffset.UtcNow);
        ICreditAccountRepository accounts = Substitute.For<ICreditAccountRepository>();
        accounts.FindByUserIdForUpdateAsync(userId, Arg.Any<CancellationToken>()).Returns(account);
        ICreditLedgerRepository ledger = Substitute.For<ICreditLedgerRepository>();
        CreditService service = new(accounts, ledger, TimeProvider.System);
        var result = await service.AdjustAsync(userId, delta, Guid.NewGuid(), Guid.NewGuid(), null, CancellationToken.None);
        result.Error!.Code.ShouldBe(Error.ConflictCode);
        account.Balance.ShouldBe(balance);
        await ledger.DidNotReceive().AddAsync(Arg.Any<CreditLedgerEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_Demoted_Actor_Cannot_Adjust_Credits_After_Waiting_For_The_Role_Lock()
    {
        ICurrentUser caller = Substitute.For<ICurrentUser>();
        caller.IsAuthenticated.Returns(true);
        caller.UserId.Returns(Guid.NewGuid());
        IUserRepository users = Substitute.For<IUserRepository>();
        ICreditService credits = Substitute.For<ICreditService>();
        var result = await new AdjustUserCreditsHandler(users, caller, credits).HandleAsync(
            new(Guid.NewGuid(), new(5, Guid.NewGuid())), CancellationToken.None);
        result.Error!.Code.ShouldBe(Error.ForbiddenCode);
        await users.Received(1).LockRoleChangesAsync(CancellationToken.None);
        await credits.DidNotReceive().AdjustAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<Guid>(),
            Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Validation_Accepts_Arbitrary_Signed_Integers_And_Rejects_Zero_Missing_Ids_And_Long_Notes()
    {
        AdjustUserCreditsValidator validator = new();
        foreach (int? delta in new int?[] { null, 0, int.MinValue })
        {
            validator.Validate(new AdjustUserCreditsCommand(Guid.NewGuid(), new(delta, Guid.NewGuid()))).IsValid.ShouldBeFalse();
        }
        validator.Validate(new AdjustUserCreditsCommand(Guid.NewGuid(), new(42, null))).IsValid.ShouldBeFalse();
        validator.Validate(new AdjustUserCreditsCommand(Guid.NewGuid(), new(42, Guid.Empty))).IsValid.ShouldBeFalse();
        validator.Validate(new AdjustUserCreditsCommand(Guid.NewGuid(), new(42, Guid.NewGuid(), new string('x', 201)))).IsValid.ShouldBeFalse();
        validator.Validate(new AdjustUserCreditsCommand(Guid.NewGuid(), new(-42, Guid.NewGuid()))).IsValid.ShouldBeTrue();
        validator.Validate(new AdjustUserCreditsCommand(Guid.NewGuid(), new(int.MaxValue, Guid.NewGuid()))).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Invalid_Directions_And_Overflow_Do_Not_Mutate_The_Aggregate()
    {
        CreditAccount account = CreditAccount.Open(Guid.NewGuid(), int.MaxValue, DateTimeOffset.UtcNow);
        Should.Throw<DomainException>(() => account.RecordTransaction(1, (CreditDelta)99, "Test", DateTimeOffset.UtcNow));
        Should.Throw<DomainException>(() => account.RecordTransaction(1, CreditDelta.Grant, "Test", DateTimeOffset.UtcNow));
        account.Balance.ShouldBe(int.MaxValue);
    }
}
