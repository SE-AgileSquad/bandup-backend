using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Writing;
using BandUp.Application.Features.Writing.Admin;
using BandUp.Application.Features.Writing.Finalization;
using BandUp.Application.Features.Writing.SubmitWritingEssay;
using BandUp.Domain.Billing;
using BandUp.Domain.Writing;
using BandUp.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BandUp.IntegrationTests;

[Collection(BandUpCollection.Name)]
public sealed class WritingAcceptanceTests(BandUpTestEnvironment environment) : WritingAcceptanceFixture(environment)
{
    private static Task<Result<WritingSubmissionAcceptedDto>> Handle(IServiceScope scope, SubmitWritingEssayCommand command) =>
        scope.ServiceProvider.GetRequiredService<ICommandHandler<SubmitWritingEssayCommand, WritingSubmissionAcceptedDto>>()
            .HandleAsync(command, Ct);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SubmissionInsertRequiresAuthorizationForTheExactAcceptedValues(bool tamperWithAcceptedRow)
    {
        await using var factory = Factory();
        var (owner, command) = await Seed(factory);
        using var scope = Scope(factory, owner);
        await Work(scope).BeginTransactionAsync(Ct);
        (await Handle(scope, command)).IsSuccess.ShouldBeTrue();
        var db = Db(scope);
        var accepted = db.WritingSubmissions.Local.Single();
        if (tamperWithAcceptedRow)
        {
            db.Entry(accepted).Property(s => s.EssayText).CurrentValue = Essay + " changed";
        }
        else
        {
            var prompt = await db.WritingPrompts.SingleAsync(p => p.Id == command.PromptId);
            db.Set<WritingSubmission>().Add(new(owner, Guid.NewGuid(), prompt, Essay,
                accepted.SubmittedAt, accepted.QueueDeadline));
        }
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Work(scope).SaveChangesAsync(Ct));
        error.Message.ShouldBe("Unauthorized Writing submission change.");

        using var fresh = Scope(factory, owner);
        var read = Db(fresh);
        (await read.WritingSubmissions.AnyAsync(s => s.UserId == owner)).ShouldBeFalse();
        var account = await read.CreditAccounts.SingleAsync(a => a.UserId == owner);
        account.Balance.ShouldBe(1);
        account.ReservedRefundCapacity.ShouldBe(0);
        var grant = await read.CreditLedger.SingleAsync(e => e.UserId == owner);
        grant.Reason.ShouldBe(CreditLedgerEntry.AdminGrantReason);
        grant.Delta.ShouldBe(1);
    }

    [Theory]
    [InlineData(nameof(WritingSubmission.UserId))]
    [InlineData(nameof(WritingSubmission.IdempotencyKey))]
    [InlineData(nameof(WritingSubmission.PromptId))]
    [InlineData("PromptTitle")]
    [InlineData("PromptText")]
    [InlineData(nameof(WritingSubmission.EssayText))]
    [InlineData(nameof(WritingSubmission.SubmittedAt))]
    [InlineData(nameof(WritingSubmission.QueueDeadline))]
    public async Task ImmutableSubmissionFieldsCannotBeWritten(string field)
    {
        await using var factory = Factory();
        var (owner, command) = await Seed(factory, 2);
        using var initial = Scope(factory, owner);
        var receipt = (await Send(initial, command)).Value;
        using var scope = Scope(factory, owner);
        await Work(scope).BeginTransactionAsync(Ct);
        var db = Db(scope);
        var original = await db.WritingSubmissions.AsNoTracking().SingleAsync(s => s.Id == receipt.SubmissionId);
        (await Handle(scope, command with { IdempotencyKey = Guid.NewGuid() })).IsSuccess.ShouldBeTrue();
        var row = await db.WritingSubmissions.SingleAsync(s => s.Id == receipt.SubmissionId);
        var property = db.Entry(row).Property(field);
        property.CurrentValue = property.CurrentValue switch
        {
            Guid => Guid.NewGuid(),
            DateTimeOffset timestamp => timestamp.AddSeconds(1),
            string value => value + " changed",
            _ => throw new InvalidOperationException($"Unexpected property {field}.")
        };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Work(scope).SaveChangesAsync(Ct));
        error.Message.ShouldBe("Unauthorized Writing submission change.");

        using var fresh = Scope(factory, owner);
        var read = Db(fresh);
        var saved = await read.WritingSubmissions.SingleAsync(s => s.UserId == owner);
        read.Entry(saved).Property(field).CurrentValue.ShouldBe(read.Entry(original).Property(field).CurrentValue);
        saved.Status.ShouldBe(WritingEvaluationStatus.Queued);
        var account = await read.CreditAccounts.SingleAsync(a => a.UserId == owner);
        account.Balance.ShouldBe(1);
        account.ReservedRefundCapacity.ShouldBe(1);
        var ledger = await read.CreditLedger.Where(e => e.UserId == owner).ToListAsync();
        ledger.Count.ShouldBe(2);
        ledger.Single(e => e.Reason == CreditLedgerEntry.WritingChargeReason).ReferenceId.ShouldBe(saved.Id);
        ledger.Sum(e => e.Delta).ShouldBe(account.Balance);
    }

    [Fact]
    public async Task ReplayUsesOriginalReceiptAfterArchivalAndTerminalTransition()
    {
        var clock = new TestClock();
        await using var factory = Factory(clock);
        var (owner, command) = await Seed(factory);
        using var first = Scope(factory, owner);
        var receipt = (await Send(first, command)).Value;
        using var edit = Scope(factory, owner);
        var db = Db(edit);
        var submission = await db.WritingSubmissions.SingleAsync(s => s.Id == receipt.SubmissionId);
        submission.SubmittedAt.ShouldBe(receipt.SubmittedAt);
        submission.QueueDeadline.ShouldBe(receipt.SubmittedAt.AddMinutes(5));
        submission.EssayText.ShouldBe(Essay);
        submission.PromptSnapshot.ShouldBe(new WritingPromptSnapshot(command.PromptId, "Cities", "Discuss growth."));
        clock.Advance(submission.QueueDeadline - clock.GetUtcNow());
        (await db.WritingPrompts.SingleAsync(p => p.Id == command.PromptId)).Archive(submission.QueueDeadline);
        await db.SaveChangesAsync();
        using var archivedReplay = Scope(factory, owner);
        (await Send(archivedReplay, command)).Value.ShouldBe(receipt);
        using var expiry = Scope(factory, owner);
        var dispatch = expiry.ServiceProvider.GetRequiredService<IDispatcher>();
        (await dispatch.SendAsync<ExpireWriting, bool>(new(receipt.SubmissionId), Ct)).Value.ShouldBeTrue();
        using var replay = Scope(factory, owner);
        (await Send(replay, command)).Value.ShouldBe(receipt);
        (await Send(replay, command with { EssayText = Essay.Normalize() })).Error!.Code.ShouldBe(WritingErrorCodes.IdempotencyConflict);
        using var other = Scope(factory, owner);
        (await Send(other, command with { PromptId = Guid.NewGuid() })).Error!.Code.ShouldBe(WritingErrorCodes.IdempotencyConflict);
        await Verify(factory, owner, receipt, expired: true);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConcurrentRequestsSerializeBeforeSecondIdempotencyCheck(bool identical)
    {
        await using var factory = Factory();
        var (owner, command) = await Seed(factory);
        using var a = Scope(factory, owner);
        using var b = Scope(factory, owner);
        await Work(a).BeginTransactionAsync(Ct);
        var receipt = (await Handle(a, command)).Value;
        await Work(b).BeginTransactionAsync(Ct);
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var waiting = Send(b, identical ? command : command with { IdempotencyKey = Guid.NewGuid() }, limit.Token);
        await WritingBillingTests.WaitForBlockAsync(Db(a), Db(b), limit.Token);
        await Work(a).SaveChangesAsync(Ct);
        await Work(a).CommitTransactionAsync(Ct);
        var result = await waiting;
        if (identical)
        {
            result.Value.ShouldBe(receipt);
        }
        else
        {
            result.Error!.Code.ShouldBe(WritingErrorCodes.InsufficientCredit);
        }
        await Verify(factory, owner, receipt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OutstandingCountIncludesPendingAndFlushedAcceptance(bool flushFirst)
    {
        await using var factory = Factory();
        var (owner, command) = await Seed(factory, 2);
        using var scope = Scope(factory, owner);
        await Work(scope).BeginTransactionAsync(Ct);
        (await Handle(scope, command)).IsSuccess.ShouldBeTrue();
        if (flushFirst)
        {
            await Work(scope).SaveChangesAsync(Ct);
        }
        (await Handle(scope, command with { IdempotencyKey = Guid.NewGuid() })).IsSuccess.ShouldBeTrue();
        await Work(scope).SaveChangesAsync(Ct);
        await Work(scope).CommitTransactionAsync(Ct);
        using var fresh = Scope(factory, owner);
        var db = Db(fresh);
        (await db.WritingSubmissions.CountAsync(s => s.UserId == owner)).ShouldBe(2);
        var account = await db.CreditAccounts.SingleAsync(a => a.UserId == owner);
        account.ReservedRefundCapacity.ShouldBe(2);
        account.Balance.ShouldBe(0);
        (await db.CreditLedger.Where(e => e.UserId == owner).SumAsync(e => e.Delta)).ShouldBe(account.Balance);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedTransactionLeavesKeyAndCreditAvailable(bool flush)
    {
        await using var factory = Factory();
        var (owner, command) = await Seed(factory);
        using var failed = Scope(factory, owner);
        await Work(failed).BeginTransactionAsync(Ct);
        var rolledBack = (await Handle(failed, command)).Value;
        if (flush)
        {
            await Work(failed).SaveChangesAsync(Ct);
        }
        await Work(failed).RollbackTransactionAsync(Ct);
        using var fresh = Scope(factory, owner);
        var db = Db(fresh);
        (await db.WritingSubmissions.AnyAsync(s => s.UserId == owner)).ShouldBeFalse();
        (await db.CreditLedger.AnyAsync(e => e.UserId == owner && e.Reason != CreditLedgerEntry.AdminGrantReason)).ShouldBeFalse();
        var account = await db.CreditAccounts.AsNoTracking().SingleAsync(a => a.UserId == owner);
        account.Balance.ShouldBe(1);
        account.ReservedRefundCapacity.ShouldBe(0);
        var receipt = (await Send(fresh, command)).Value;
        receipt.SubmissionId.ShouldNotBe(rolledBack.SubmissionId);
        await Verify(factory, owner, receipt);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ArchivalSerializesWithAcceptance(bool archiveFirst)
    {
        await using var factory = Factory();
        var (owner, command) = await Seed(factory);
        using var first = Scope(factory, owner);
        using var second = Scope(factory, owner);
        await Work(first).BeginTransactionAsync(Ct);
        await Work(second).BeginTransactionAsync(Ct);
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        async Task Archive(IServiceScope scope)
        {
            var admin = scope.ServiceProvider.GetRequiredService<IAdminWritingPromptStore>();
            var prompt = await admin.LoadForUpdateAsync(command.PromptId, limit.Token);
            prompt!.Archive(DateTimeOffset.UtcNow);
        }

        if (archiveFirst)
        {
            await Archive(first);
            var waiting = Send(second, command, limit.Token);
            await WritingBillingTests.WaitForBlockAsync(Db(first), Db(second), limit.Token);
            await Work(first).SaveChangesAsync(Ct);
            await Work(first).CommitTransactionAsync(Ct);
            (await waiting).Error!.Code.ShouldBe(Error.NotFoundCode);
            using var check = Scope(factory, owner);
            (await Db(check).WritingSubmissions.AnyAsync(s => s.UserId == owner)).ShouldBeFalse();
            (await Db(check).CreditAccounts.SingleAsync(a => a.UserId == owner)).Balance.ShouldBe(1);
        }
        else
        {
            var receipt = (await Handle(first, command)).Value;
            var waiting = Archive(second);
            await WritingBillingTests.WaitForBlockAsync(Db(first), Db(second), limit.Token);
            await Work(first).SaveChangesAsync(Ct);
            await Work(first).CommitTransactionAsync(Ct);
            await waiting;
            await Work(second).SaveChangesAsync(Ct);
            await Work(second).CommitTransactionAsync(Ct);
            await Verify(factory, owner, receipt);
        }
    }

    [Fact]
    public async Task IndependentOwnersMayReuseTheKey()
    {
        await using var factory = Factory();
        var (owner, command) = await Seed(factory);
        var (second, _) = await Seed(factory);
        using var a = Scope(factory, owner);
        using var b = Scope(factory, second);
        var first = (await Send(a, command)).Value;
        var other = (await Send(b, command)).Value;
        first.SubmissionId.ShouldNotBe(other.SubmissionId);
        await Verify(factory, owner, first);
        await Verify(factory, second, other);
    }

    [Theory]
    [InlineData("words", Error.ValidationCode)]
    [InlineData("prompt", Error.NotFoundCode)]
    [InlineData("balance", WritingErrorCodes.InsufficientCredit)]
    [InlineData("missing", null)]
    public async Task RejectionsLeaveNoAcceptedRowOrDebit(string scenario, string? expected)
    {
        await using var factory = Factory();
        var (owner, command) = await Seed(factory, scenario == "missing" ? null : scenario == "balance" ? 0 : 1);
        using var request = Scope(factory, owner);
        if (scenario == "missing")
        {
            await Should.ThrowAsync<InvalidOperationException>(() => Send(request, command));
        }
        else
        {
            command = scenario switch
            {
                "words" => command with { EssayText = "Short" },
                "prompt" => command with { PromptId = Guid.NewGuid() },
                _ => command,
            };
            (await Send(request, command)).Error!.Code.ShouldBe(expected);
        }
        using var check = Scope(factory, owner);
        (await Db(check).CreditLedger.AnyAsync(e => e.UserId == owner && e.Reason != CreditLedgerEntry.AdminGrantReason)).ShouldBeFalse();
        (await Db(check).WritingSubmissions.AnyAsync(s => s.UserId == owner)).ShouldBeFalse();
    }

    private static async Task Verify(BandUpApiFactory factory, Guid owner, WritingSubmissionAcceptedDto receipt, bool expired = false)
    {
        using var scope = Scope(factory, owner);
        var db = Db(scope);
        var submission = await db.WritingSubmissions.SingleAsync(s => s.UserId == owner);
        submission.Id.ShouldBe(receipt.SubmissionId);
        submission.SubmittedAt.ShouldBe(receipt.SubmittedAt);
        var ledger = await db.CreditLedger.Where(e => e.UserId == owner).ToListAsync();
        var debit = ledger.Single(e => e.Reason == CreditLedgerEntry.WritingChargeReason);
        debit.Reason.ShouldBe(CreditLedgerEntry.WritingChargeReason);
        debit.ReferenceId.ShouldBe(submission.Id);
        debit.Delta.ShouldBe(-1);
        var account = await db.CreditAccounts.SingleAsync(a => a.UserId == owner);
        account.Balance.ShouldBe(expired ? 1 : 0);
        ledger.Sum(e => e.Delta).ShouldBe(account.Balance);
        account.ReservedRefundCapacity.ShouldBe(expired ? 0 : 1);
        submission.Status.ShouldBe(expired ? WritingEvaluationStatus.Failed : WritingEvaluationStatus.Queued);
        if (expired)
        {
            var refund = ledger.Single(e => e.Reason == CreditLedgerEntry.WritingRefundReason);
            refund.ReferenceId.ShouldBe(submission.Id);
            refund.Delta.ShouldBe(1);
            (await db.WritingFeedbacks.AnyAsync(f => f.Id == submission.Id)).ShouldBeFalse();
        }
    }
}
