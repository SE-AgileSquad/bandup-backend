using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Domain.Billing;
using BandUp.Domain.Common;
using BandUp.Domain.Identity;
using BandUp.Infrastructure.Persistence;
using BandUp.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;

namespace BandUp.IntegrationTests;

[Collection(BandUpCollection.Name)]
public sealed class WritingBillingTests(BandUpTestEnvironment environment)
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RolledBackChargeCannotEscapeIntoNextTransaction(bool saveFirst, bool sameAccount)
    {
        await using BandUpApiFactory factory = new(environment);
        Guid first = await SeedAsync(factory);
        Guid second = sameAccount ? first : await SeedAsync(factory);
        using var scope = factory.Services.CreateScope();
        var work = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var credit = scope.ServiceProvider.GetRequiredService<ICreditService>();
        await work.BeginTransactionAsync(Ct);
        (await credit.ChargeWritingAsync(first, Guid.NewGuid(), Ct)).IsSuccess.ShouldBeTrue();
        if (saveFirst)
        {
            await work.SaveChangesAsync(Ct);
        }
        await work.RollbackTransactionAsync(Ct);
        await Should.ThrowAsync<ObjectDisposedException>(() => work.BeginTransactionAsync(Ct));
        using var retry = factory.Services.CreateScope();
        work = retry.ServiceProvider.GetRequiredService<IUnitOfWork>();
        credit = retry.ServiceProvider.GetRequiredService<ICreditService>();
        await work.BeginTransactionAsync(Ct);
        Guid valid = Guid.NewGuid();
        (await credit.ChargeWritingAsync(second, valid, Ct)).IsSuccess.ShouldBeTrue();
        await work.SaveChangesAsync(Ct);
        await work.CommitTransactionAsync(Ct);
        if (!sameAccount)
        {
            await VerifyAsync(factory, first, 1);
        }
        await VerifyAsync(factory, second, 0, valid);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task CompetingOperationsObserveCommittedBalanceAndLedger(bool refund, bool sameReference)
    {
        await using BandUpApiFactory factory = new(environment);
        Guid owner = await SeedAsync(factory);
        Guid reference = Guid.NewGuid();
        using var first = factory.Services.CreateScope();
        using var second = factory.Services.CreateScope();
        var firstDb = first.ServiceProvider.GetRequiredService<BandUpDbContext>();
        var secondDb = second.ServiceProvider.GetRequiredService<BandUpDbContext>();
        var firstCredit = first.ServiceProvider.GetRequiredService<ICreditService>();
        var secondCredit = second.ServiceProvider.GetRequiredService<ICreditService>();
        if (refund)
        {
            await using var setup = await firstDb.Database.BeginTransactionAsync();
            (await firstCredit.ChargeWritingAsync(owner, reference, Ct)).IsSuccess.ShouldBeTrue();
            await firstDb.SaveChangesAsync();
            await setup.CommitAsync();
        }

        await using var tx1 = await firstDb.Database.BeginTransactionAsync();
        await firstCredit.LockAccountAsync(owner, Ct);
        await using var tx2 = await secondDb.Database.BeginTransactionAsync();
        var stale = await secondDb.CreditAccounts.SingleAsync(account => account.UserId == owner);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var waiting = secondCredit.LockAccountAsync(owner, timeout.Token);
        await WaitForBlockAsync(firstDb, secondDb, timeout.Token);
        (await ApplyAsync(firstCredit, reference)).IsSuccess.ShouldBeTrue();
        await firstDb.SaveChangesAsync();
        await tx1.CommitAsync();
        await waiting;
        Result result = await ApplyAsync(secondCredit, sameReference ? reference : Guid.NewGuid());
        result.IsSuccess.ShouldBe(sameReference);
        if (!sameReference)
        {
            result.Error!.Code.ShouldBe("INSUFFICIENT_CREDIT");
        }
        stale.Balance.ShouldBe(refund ? 1 : 0);
        await secondDb.SaveChangesAsync();
        await tx2.CommitAsync();
        await VerifyAsync(factory, owner, refund ? 1 : 0, reference, refund);
        if (refund)
        {
            await using var replay = await secondDb.Database.BeginTransactionAsync();
            (await secondCredit.ChargeWritingAsync(owner, reference, Ct)).IsSuccess.ShouldBeTrue();
            await secondDb.SaveChangesAsync();
            await replay.CommitAsync();
            await VerifyAsync(factory, owner, 1, reference, refunded: true);
        }

        Task<Result> ApplyAsync(ICreditService service, Guid id) => refund
            ? service.RefundWritingAsync(owner, id, Ct)
            : service.ChargeWritingAsync(owner, id, Ct);
    }

    [Fact]
    public async Task ChargeAndRefundFollowCallerRollback()
    {
        await using BandUpApiFactory factory = new(environment);
        Guid owner = await SeedAsync(factory);
        Guid reference = Guid.NewGuid();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BandUpDbContext>();
        var credit = scope.ServiceProvider.GetRequiredService<ICreditService>();
        await Should.ThrowAsync<InvalidOperationException>(() => credit.LockAccountAsync(owner, Ct));
        await using var tx = await db.Database.BeginTransactionAsync();
        (await credit.ChargeWritingAsync(owner, reference, Ct)).IsSuccess.ShouldBeTrue();
        (await credit.ChargeWritingAsync(owner, reference, Ct)).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync();
        (await db.CreditAccounts.SingleAsync(row => row.UserId == owner)).Balance.ShouldBe(0);
        (await credit.RefundWritingAsync(owner, reference, Ct)).IsSuccess.ShouldBeTrue();
        (await credit.RefundWritingAsync(owner, reference, Ct)).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync();
        (await db.CreditLedger.CountAsync(row => row.UserId == owner && row.ReferenceId == reference)).ShouldBe(2);
        await tx.RollbackAsync();
        await VerifyAsync(factory, owner, 1);
    }

    private static async Task WaitForBlockAsync(BandUpDbContext holder, BandUpDbContext waiter, CancellationToken ct)
    {
        int pid = ((NpgsqlConnection)waiter.Database.GetDbConnection()).ProcessID;
        while (!await holder.Database.SqlQuery<bool>(
            $"SELECT pg_backend_pid() = ANY(pg_blocking_pids({pid})) AS \"Value\"").SingleAsync(ct))
        {
            await Task.Delay(10, ct);
        }
    }

    [Fact]
    public async Task CrossAccountChargeCollisionRetiresLosingContext()
    {
        await using BandUpApiFactory factory = new(environment);
        Guid winner = await SeedAsync(factory);
        Guid loser = await SeedAsync(factory);
        Guid reference = Guid.NewGuid();
        using var a = factory.Services.CreateScope();
        using var b = factory.Services.CreateScope();
        var first = a.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var second = b.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var losingCredit = b.ServiceProvider.GetRequiredService<ICreditService>();
        await first.BeginTransactionAsync(Ct);
        await second.BeginTransactionAsync(Ct);
        (await a.ServiceProvider.GetRequiredService<ICreditService>().ChargeWritingAsync(winner, reference, Ct)).IsSuccess.ShouldBeTrue();
        (await losingCredit.ChargeWritingAsync(loser, reference, Ct)).IsSuccess.ShouldBeTrue();
        await first.SaveChangesAsync(Ct);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var waiting = second.SaveChangesAsync(timeout.Token);
        await WaitForBlockAsync(a.ServiceProvider.GetRequiredService<BandUpDbContext>(),
            b.ServiceProvider.GetRequiredService<BandUpDbContext>(), timeout.Token);
        await first.CommitTransactionAsync(Ct);
        var error = await Should.ThrowAsync<DuplicateEntityException>(() => waiting);
        var pg = error.InnerException!.InnerException.ShouldBeOfType<PostgresException>();
        pg.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        pg.ConstraintName.ShouldBe("ux_ledger_reason_reference");
        await Should.ThrowAsync<ObjectDisposedException>(() => losingCredit.LockAccountAsync(loser, Ct));
        await Should.ThrowAsync<ObjectDisposedException>(() => second.BeginTransactionAsync(Ct));
        await VerifyAsync(factory, winner, 0, reference);
        await VerifyAsync(factory, loser, 1);
        using var retry = factory.Services.CreateScope();
        var work = retry.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await work.BeginTransactionAsync(Ct);
        Guid other = Guid.NewGuid();
        (await retry.ServiceProvider.GetRequiredService<ICreditService>().ChargeWritingAsync(loser, other, Ct)).IsSuccess.ShouldBeTrue();
        await work.SaveChangesAsync(Ct);
        await work.CommitTransactionAsync(Ct);
        await VerifyAsync(factory, loser, 0, other);
    }

    [Fact]
    public async Task CancelledLockAcquisitionDoesNotCacheAnAccount()
    {
        await using BandUpApiFactory factory = new(environment);
        Guid owner = await SeedAsync(factory);
        using var a = factory.Services.CreateScope();
        using var b = factory.Services.CreateScope();
        var holder = a.ServiceProvider.GetRequiredService<BandUpDbContext>();
        var waiter = b.ServiceProvider.GetRequiredService<BandUpDbContext>();
        var work = b.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var credit = b.ServiceProvider.GetRequiredService<ICreditService>();
        await using var tx = await holder.Database.BeginTransactionAsync();
        await a.ServiceProvider.GetRequiredService<ICreditService>().LockAccountAsync(owner, Ct);
        await work.BeginTransactionAsync(Ct);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var waiting = credit.ChargeWritingAsync(owner, Guid.NewGuid(), timeout.Token);
        await WaitForBlockAsync(holder, waiter, timeout.Token);
        await timeout.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => waiting);
        waiter.ChangeTracker.Entries().ShouldBeEmpty();
        // Diagnostic probe only: a cache hit would incorrectly return an account despite
        // PostgreSQL having aborted the transaction on cancellation.
        var error = await Should.ThrowAsync<PostgresException>(() => credit.LockAccountAsync(owner, Ct));
        error.SqlState.ShouldBe(PostgresErrorCodes.InFailedSqlTransaction);
        await work.RollbackTransactionAsync(timeout.Token);
        await Should.ThrowAsync<ObjectDisposedException>(() => work.BeginTransactionAsync(Ct));
        await tx.CommitAsync();
        await VerifyAsync(factory, owner, 1);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    public async Task RefundRejectsMalformedLedgerEvidence(bool persisted, bool malformedRefund, bool foreignOwner)
    {
        await using BandUpApiFactory factory = new(environment);
        Guid owner = await SeedAsync(factory);
        Guid source = foreignOwner ? await SeedAsync(factory) : owner;
        Guid reference = Guid.NewGuid();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BandUpDbContext>();
        var credit = scope.ServiceProvider.GetRequiredService<ICreditService>();
        await using var tx = await db.Database.BeginTransactionAsync();
        await credit.LockAccountAsync(owner, Ct);
        var account = await db.CreditAccounts.SingleAsync(a => a.UserId == owner);
        if (malformedRefund)
        {
            (await credit.ChargeWritingAsync(owner, reference, Ct)).IsSuccess.ShouldBeTrue();
        }
        var sourceAccount = await db.CreditAccounts.SingleAsync(a => a.UserId == source);
        var reason = malformedRefund ? CreditLedgerEntry.WritingRefundReason : CreditLedgerEntry.WritingChargeReason;
        var direction = foreignOwner && !malformedRefund ? CreditDelta.Charge : CreditDelta.Grant;
        db.Add(sourceAccount.RecordTransaction(foreignOwner ? 1 : 2, direction, reason, DateTimeOffset.UtcNow, reference));
        if (persisted)
        {
            await db.SaveChangesAsync();
        }
        int balance = account.Balance;
        int count = db.CreditLedger.Local.Count;
        (await credit.RefundWritingAsync(owner, reference, Ct)).Error!.Type.ShouldBe(ErrorType.Conflict);
        account.Balance.ShouldBe(balance);
        db.CreditLedger.Local.Count.ShouldBe(count);
        await tx.RollbackAsync();
        await VerifyAsync(factory, owner, 1);
    }

    private static async Task<Guid> SeedAsync(BandUpApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BandUpDbContext>();
        var now = DateTimeOffset.UtcNow;
        var user = User.Create($"{Guid.NewGuid():N}@example.test", "Learner", "test-hash", now);
        var account = CreditAccount.Open(user.Id, 0, now);
        db.AddRange(user, account, account.RecordTransaction(1, CreditDelta.Grant, CreditLedgerEntry.AdminGrantReason, now));
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static async Task VerifyAsync(BandUpApiFactory factory, Guid owner, int balance,
        Guid? reference = null, bool refunded = false)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BandUpDbContext>();
        (await db.CreditAccounts.SingleAsync(row => row.UserId == owner)).Balance.ShouldBe(balance);
        var ledger = await db.CreditLedger.Where(row => row.UserId == owner).ToListAsync();
        var writing = ledger.Where(row => row.ReferenceId.HasValue).ToList();
        writing.Count.ShouldBe(reference is null ? 0 : refunded ? 2 : 1);
        writing.ShouldAllBe(row => row.UserId == owner && row.ReferenceId == reference);
        if (reference.HasValue)
        {
            writing.Single(row => row.Reason == CreditLedgerEntry.WritingChargeReason).Delta.ShouldBe(-1);
        }
        if (refunded)
        {
            writing.Single(row => row.Reason == CreditLedgerEntry.WritingRefundReason).Delta.ShouldBe(1);
        }
        ledger.Sum(row => row.Delta).ShouldBe(balance);
    }
}
