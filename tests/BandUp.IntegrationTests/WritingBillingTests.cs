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
        // Simulate malformed historical evidence without minting an invalid Writing transaction.
        var malformed = sourceAccount.RecordTransaction(foreignOwner ? 1 : 2, direction,
            CreditLedgerEntry.AdminAdjustReason, DateTimeOffset.UtcNow, reference);
        db.Add(malformed).Property(e => e.Reason).CurrentValue = reason;
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

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task AdminAndWritingSerializeOnTheSameAccount(bool adminFirst, bool refund)
    {
        await using BandUpApiFactory factory = new(environment);
        Guid owner = await SeedAsync(factory);
        Guid actor = await SeedAsync(factory);
        Guid reference = Guid.NewGuid();
        using var first = factory.Services.CreateScope();
        using var second = factory.Services.CreateScope();
        var a = first.ServiceProvider.GetRequiredService<ICreditService>();
        var b = second.ServiceProvider.GetRequiredService<ICreditService>();
        var holder = first.ServiceProvider.GetRequiredService<BandUpDbContext>();
        var waiter = second.ServiceProvider.GetRequiredService<BandUpDbContext>();
        if (refund)
        {
            await using var setup = await holder.Database.BeginTransactionAsync();
            (await a.ChargeWritingAsync(owner, reference, Ct)).IsSuccess.ShouldBeTrue();
            await holder.SaveChangesAsync();
            await setup.CommitAsync();
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        async Task Apply(ICreditService service, bool admin)
        {
            Result result = admin
                ? await service.AdjustAsync(owner, 2, actor, reference, "review", timeout.Token)
                : refund ? await service.RefundWritingAsync(owner, reference, timeout.Token)
                         : await service.ChargeWritingAsync(owner, reference, timeout.Token);
            result.IsSuccess.ShouldBeTrue();
        }

        await using var tx1 = await holder.Database.BeginTransactionAsync();
        await Apply(a, adminFirst);
        await using var tx2 = await waiter.Database.BeginTransactionAsync();
        var waiting = Apply(b, !adminFirst);
        // No UPDATE has run: only the shared SELECT FOR UPDATE can block the second operation.
        await WaitForBlockAsync(holder, waiter, timeout.Token);
        await holder.SaveChangesAsync();
        await tx1.CommitAsync();
        await waiting;
        await waiter.SaveChangesAsync();
        await tx2.CommitAsync();
        using var check = factory.Services.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<BandUpDbContext>();
        var entries = await db.CreditLedger.Where(e => e.UserId == owner).ToListAsync();
        entries.Count.ShouldBe(refund ? 4 : 3);
        var referenced = entries.Where(e => e.ReferenceId == reference).ToList();
        referenced.Count.ShouldBe(entries.Count - 1);
        var adjustment = referenced.Single(e => e.Reason == CreditLedgerEntry.AdminAdjustReason);
        adjustment.CreatedByAdminId.ShouldBe(actor);
        adjustment.Note.ShouldBe("review");
        adjustment.Delta.ShouldBe(2);
        referenced.Single(e => e.Reason == CreditLedgerEntry.WritingChargeReason).Delta.ShouldBe(-1);
        if (refund)
        {
            referenced.Single(e => e.Reason == CreditLedgerEntry.WritingRefundReason).Delta.ShouldBe(1);
        }
        entries.Sum(e => e.Delta).ShouldBe(refund ? 3 : 2);
        var account = await db.CreditAccounts.SingleAsync(e => e.UserId == owner);
        account.Balance.ShouldBe(entries.Sum(e => e.Delta));
        account.ReservedRefundCapacity.ShouldBe(refund ? 0 : 1);
    }

    [Fact]
    public async Task MixedOperationsPreservePendingChangesAndReplayAcrossFlushAndRollback()
    {
        await using BandUpApiFactory factory = new(environment);
        Guid owner = await SeedAsync(factory);
        Guid actor = await SeedAsync(factory);
        Guid reference = Guid.NewGuid();
        foreach (bool commit in new[] { false, true })
        {
            using var scope = factory.Services.CreateScope();
            var work = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var credit = scope.ServiceProvider.GetRequiredService<ICreditService>();
            await work.BeginTransactionAsync(Ct);
            var adjustment = await credit.AdjustAsync(owner, 1, actor, reference, "retry", Ct);
            adjustment.Value.IsReplay.ShouldBeFalse();
            (await credit.ChargeWritingAsync(owner, reference, Ct)).IsSuccess.ShouldBeTrue();
            (await credit.AdjustAsync(owner, 1, actor, reference, "retry", Ct)).Value.IsReplay.ShouldBeTrue();
            await work.SaveChangesAsync(Ct);
            (await credit.RefundWritingAsync(owner, reference, Ct)).IsSuccess.ShouldBeTrue();
            var replay = await credit.AdjustAsync(owner, 1, actor, reference, "retry", Ct);
            replay.Value.IsReplay.ShouldBeTrue();
            replay.Value.Balance.ShouldBe(2);
            await work.SaveChangesAsync(Ct);
            if (commit)
            {
                await work.CommitTransactionAsync(Ct);
            }
            else
            {
                await work.RollbackTransactionAsync(Ct);
            }
        }
        using var verification = factory.Services.CreateScope();
        var db = verification.ServiceProvider.GetRequiredService<BandUpDbContext>();
        (await db.CreditLedger.CountAsync(e => e.ReferenceId == reference)).ShouldBe(3);
        (await db.CreditAccounts.SingleAsync(e => e.UserId == owner)).Balance.ShouldBe(2);
    }

    [Fact]
    public async Task AdminCannotSpendReservedHeadroomAndRolledBackRefundCanRetry()
    {
        await using BandUpApiFactory factory = new(environment);
        Guid owner = await SeedAsync(factory);
        Guid actor = await SeedAsync(factory);
        Guid reference = Guid.NewGuid();

        async Task RunAsync(Func<ICreditService, Task<Result>> operation, bool success, bool rollback = false)
        {
            using var scope = factory.Services.CreateScope();
            var work = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await work.BeginTransactionAsync(Ct);
            var result = await operation(scope.ServiceProvider.GetRequiredService<ICreditService>());
            result.IsSuccess.ShouldBe(success);
            if (!success)
            {
                result.Error!.Type.ShouldBe(ErrorType.Conflict);
            }
            await work.SaveChangesAsync(Ct);
            if (rollback || !success)
            {
                await work.RollbackTransactionAsync(Ct);
            }
            else
            {
                await work.CommitTransactionAsync(Ct);
            }
        }

        await RunAsync(c => c.ChargeWritingAsync(owner, reference, Ct), true);
        await VerifyAsync(factory, owner, 0, reference);
        await RunAsync(async c => await c.AdjustAsync(owner, int.MaxValue, actor, Guid.NewGuid(), "too much", Ct), false);
        await RunAsync(async c => await c.AdjustAsync(owner, int.MaxValue - 1, actor, Guid.NewGuid(), "fits", Ct), true);
        await RunAsync(c => c.RefundWritingAsync(owner, reference, Ct), true, rollback: true);
        using var check = factory.Services.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<BandUpDbContext>();
        var before = await db.CreditAccounts.AsNoTracking().SingleAsync(a => a.UserId == owner);
        before.ReservedRefundCapacity.ShouldBe(1);
        before.Balance.ShouldBe(int.MaxValue - 1);
        await RunAsync(c => c.RefundWritingAsync(owner, reference, Ct), true);
        await RunAsync(c => c.RefundWritingAsync(owner, reference, Ct), true);
        var after = await db.CreditAccounts.AsNoTracking().SingleAsync(a => a.UserId == owner);
        after.Balance.ShouldBe(int.MaxValue);
        after.ReservedRefundCapacity.ShouldBe(0);
        var ledger = await db.CreditLedger.Where(e => e.UserId == owner).ToListAsync();
        ledger.Count.ShouldBe(4);
        ledger.Sum(e => (long)e.Delta).ShouldBe((long)int.MaxValue);
    }

    [Fact]
    public async Task TwoOutstandingChargesPersistAndMissingCapacityBlocksRefund()
    {
        await using BandUpApiFactory factory = new(environment);
        Guid owner = await SeedAsync(factory);
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        using var setup = factory.Services.CreateScope();
        var db = setup.ServiceProvider.GetRequiredService<BandUpDbContext>();
        var credit = setup.ServiceProvider.GetRequiredService<ICreditService>();
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            (await credit.AdjustAsync(owner, 1, owner, Guid.NewGuid(), "test funding", Ct)).IsSuccess.ShouldBeTrue();
            (await credit.ChargeWritingAsync(owner, first, Ct)).IsSuccess.ShouldBeTrue();
            (await credit.ChargeWritingAsync(owner, second, Ct)).IsSuccess.ShouldBeTrue();
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        var stored = await db.CreditAccounts.AsNoTracking().SingleAsync(a => a.UserId == owner);
        stored.ReservedRefundCapacity.ShouldBe(2);
        stored.Balance.ShouldBe(0);

        // Model a pre-reservation charge history. The database permits zero headroom;
        // the service must still reject the refund without issuing credit.
        await db.Database.ExecuteSqlAsync($"UPDATE billing.credit_accounts SET reserved_refund_capacity = 0 WHERE user_id = {owner}");
        using var retry = factory.Services.CreateScope();
        var retryDb = retry.ServiceProvider.GetRequiredService<BandUpDbContext>();
        await using var refund = await retryDb.Database.BeginTransactionAsync();
        var result = await retry.ServiceProvider.GetRequiredService<ICreditService>().RefundWritingAsync(owner, second, Ct);
        result.Error!.Type.ShouldBe(ErrorType.Conflict);
        retryDb.ChangeTracker.HasChanges().ShouldBeFalse();
        await refund.RollbackAsync();
        (await db.CreditLedger.CountAsync(e => e.ReferenceId == second)).ShouldBe(1);
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
        var account = await db.CreditAccounts.SingleAsync(row => row.UserId == owner);
        account.Balance.ShouldBe(balance);
        account.ReservedRefundCapacity.ShouldBe(reference.HasValue && !refunded ? 1 : 0);
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
