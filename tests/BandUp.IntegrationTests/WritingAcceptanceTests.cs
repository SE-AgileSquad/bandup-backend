using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Writing;
using BandUp.Application.Features.Writing.Admin;
using BandUp.Application.Features.Writing.SubmitWritingEssay;
using BandUp.Domain.Billing;
using BandUp.Domain.Identity;
using BandUp.Domain.Writing;
using BandUp.Infrastructure.Persistence;
using BandUp.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;

namespace BandUp.IntegrationTests;

[Collection(BandUpCollection.Name)]
public sealed class WritingAcceptanceTests(BandUpTestEnvironment environment)
{
    private static readonly string Essay = string.Join(" ", Enumerable.Repeat("cafe\u0301", 100)) + "\n";
    private static readonly CancellationToken Ct = CancellationToken.None;

    private BandUpApiFactory Factory() => new(environment, configureServices: services =>
        services.AddScoped(_ => Substitute.For<ICurrentUser>()));

    private static IServiceScope Scope(BandUpApiFactory factory, Guid owner)
    {
        var scope = factory.Services.CreateScope();
        var caller = scope.ServiceProvider.GetRequiredService<ICurrentUser>();
        caller.IsAuthenticated.Returns(true);
        caller.UserId.Returns(owner);
        return scope;
    }

    private static BandUpDbContext Db(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<BandUpDbContext>();
    private static IUnitOfWork Work(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

    private static Task<Result<WritingSubmissionAcceptedDto>> Send(IServiceScope scope, SubmitWritingEssayCommand command,
        CancellationToken ct = default) => scope.ServiceProvider.GetRequiredService<IDispatcher>()
            .SendAsync<SubmitWritingEssayCommand, WritingSubmissionAcceptedDto>(command, ct);

    private static Task<Result<WritingSubmissionAcceptedDto>> Handle(IServiceScope scope, SubmitWritingEssayCommand command) =>
        scope.ServiceProvider.GetRequiredService<ICommandHandler<SubmitWritingEssayCommand, WritingSubmissionAcceptedDto>>()
            .HandleAsync(command, Ct);

    private static async Task<(Guid Owner, SubmitWritingEssayCommand Command)> Seed(BandUpApiFactory factory, int? balance = 1)
    {
        using var scope = factory.Services.CreateScope();
        var db = Db(scope);
        var now = DateTimeOffset.UnixEpoch;
        var user = User.Create($"{Guid.NewGuid():N}@example.test", "Learner", "hash", now);
        var prompt = WritingPrompt.CreateDraft("Cities", "Discuss growth.", "Opinion", 100, 350, user.Id, now);
        prompt.Publish(now);
        db.AddRange(user, prompt);
        if (balance.HasValue)
        {
            db.Add(CreditAccount.Open(user.Id, balance.Value, now));
        }
        await db.SaveChangesAsync();
        return (user.Id, new(Guid.NewGuid(), prompt.Id, Essay));
    }

    [Fact]
    public async Task ReplayUsesOriginalReceiptAfterArchivalAndTerminalTransition()
    {
        await using var factory = Factory();
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
        submission.Fail(submission.QueueDeadline);
        (await db.WritingPrompts.SingleAsync(p => p.Id == command.PromptId)).Archive(submission.QueueDeadline);
        await db.SaveChangesAsync();
        using var replay = Scope(factory, owner);
        (await Send(replay, command)).Value.ShouldBe(receipt);
        (await Send(replay, command with { EssayText = Essay.Normalize() })).Error!.Code.ShouldBe(WritingErrorCodes.IdempotencyConflict);
        using var other = Scope(factory, owner);
        (await Send(other, command with { PromptId = Guid.NewGuid() })).Error!.Code.ShouldBe(WritingErrorCodes.IdempotencyConflict);
        await Verify(factory, owner, receipt);
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
        (await db.CreditLedger.AnyAsync(e => e.UserId == owner)).ShouldBeFalse();
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
        (await Db(check).CreditLedger.AnyAsync(e => e.UserId == owner)).ShouldBeFalse();
        (await Db(check).WritingSubmissions.AnyAsync(s => s.UserId == owner)).ShouldBeFalse();
    }

    private static async Task Verify(BandUpApiFactory factory, Guid owner, WritingSubmissionAcceptedDto receipt)
    {
        using var scope = Scope(factory, owner);
        var db = Db(scope);
        var submission = await db.WritingSubmissions.SingleAsync(s => s.UserId == owner);
        submission.Id.ShouldBe(receipt.SubmissionId);
        submission.SubmittedAt.ShouldBe(receipt.SubmittedAt);
        var debit = await db.CreditLedger.SingleAsync(e => e.UserId == owner);
        debit.Reason.ShouldBe(CreditLedgerEntry.WritingChargeReason);
        debit.ReferenceId.ShouldBe(submission.Id);
        debit.Delta.ShouldBe(-1);
        var account = await db.CreditAccounts.SingleAsync(a => a.UserId == owner);
        account.Balance.ShouldBe(0);
        account.ReservedRefundCapacity.ShouldBe(1);
    }
}
