using BandUp.Application.Abstractions;
using BandUp.Application.Features.Billing;
using BandUp.Domain.Billing;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace BandUp.UnitTests.Features.Billing;

public sealed class CreditServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _time = new(Now);
    private readonly TestCreditAccountRepository _repository = new();
    private readonly TestUnitOfWork _unitOfWork = new();

    [Fact]
    public async Task OpenSignupAccount_Creates_An_Account_With_Initial_Balance()
    {
        CreditService service = new(_repository, _unitOfWork, _time);
        Guid userId = Guid.CreateVersion7();

        var result = await service.OpenSignupAccountAsync(userId, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        _repository.AddedAccount.ShouldNotBeNull();
        _repository.AddedAccount!.UserId.ShouldBe(userId);
        _repository.AddedAccount.Balance.ShouldBe(100.00m);
        _repository.AddedAccount.CreatedAt.ShouldBe(Now);
        _unitOfWork.SavedChanges.ShouldBeTrue();
    }

    private sealed class TestCreditAccountRepository : ICreditAccountRepository
    {
        public CreditAccount? AddedAccount { get; private set; }
        public Task AddAsync(CreditAccount account, CancellationToken ct)
        {
            AddedAccount = account;
            return Task.CompletedTask;
        }
    }

    private sealed class TestUnitOfWork : IUnitOfWork
    {
        public bool SavedChanges { get; private set; }
        public Task<int> SaveChangesAsync(CancellationToken ct)
        {
            SavedChanges = true;
            return Task.FromResult(0);
        }

        public Task BeginTransactionAsync(CancellationToken ct) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken ct) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
