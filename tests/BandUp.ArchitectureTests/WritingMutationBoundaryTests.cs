using System.Reflection;
using BandUp.Application.Abstractions;
using BandUp.Application.Features.Billing;
using BandUp.Domain.Billing;
using BandUp.Domain.Writing;
using BandUp.Infrastructure.Persistence;
using Mono.Cecil;

namespace BandUp.ArchitectureTests;

public sealed class WritingMutationBoundaryTests
{
    [Fact]
    public void RawWritingMutationsAreOutsideThePublicApi()
    {
        Assert.Null(typeof(ICreditService).GetMethod("ChargeWritingAsync"));
        Assert.Null(typeof(CreditService).GetMethod("ChargeWritingAsync"));
        Assert.Null(typeof(CreditAccount).GetMethod("RecordWritingCharge"));
        Assert.Null(typeof(CreditAccount).GetMethod("RecordWritingRefund"));
        Assert.Null(typeof(WritingSubmission).GetMethod("Succeed"));
        Assert.Null(typeof(WritingSubmission).GetMethod("Fail"));
        Assert.Null(typeof(BandUpDbContext).GetProperty("WritingFeedbacks"));
        var writer = typeof(CreditService).Assembly.GetType("BandUp.Application.Features.Writing.IWritingMutationWriter", true);
        Assert.False(writer!.IsVisible);
        Assert.Null(typeof(BandUpDbContext).GetMethod("AllowWritingLifecycleChange"));
        Assert.Null(typeof(BandUpDbContext).GetMethod("AllowWritingFeedbackInsert"));
        Assert.Null(typeof(BandUpDbContext).GetMethod("AllowWritingSubmissionInsert"));
    }

    [Theory]
    [InlineData("WritingAcceptance", "PostChargeAsync", "SubmitWritingEssayHandler")]
    [InlineData("WritingSettlement", "PostRefundAsync", "WritingSettlementHandler")]
    [InlineData("CreditAccount", "RecordWritingCharge", "CreditService")]
    [InlineData("CreditAccount", "RecordWritingRefund", "CreditService")]
    [InlineData("CreditAccount", "ReleaseWritingRefundCapacity", "WritingSettlementHandler")]
    [InlineData("WritingSubmission", "Succeed", "WritingSettlementHandler")]
    [InlineData("WritingSubmission", "Fail", "WritingSettlementHandler")]
    [InlineData("IWritingMutationWriter", "AddFeedbackAsync", "WritingSettlementHandler")]
    [InlineData("IWritingMutationWriter", "AddAcceptedSubmissionAsync", "SubmitWritingEssayHandler")]
    [InlineData("IWritingMutationWriter", "AuthorizeLifecycleChange", "ClaimWritingHandler,WritingSettlementHandler")]
    [InlineData("BandUpDbContext", "AllowWritingLifecycleChange", "EfWritingSubmissionStore")]
    [InlineData("BandUpDbContext", "AllowWritingFeedbackInsert", "EfWritingSubmissionStore")]
    [InlineData("BandUpDbContext", "AllowWritingSubmissionInsert", "EfWritingSubmissionStore")]
    [InlineData("BandUpDbContext", "get_WritingFeedbacks", "EfWritingSubmissionStore")]
    public void OnlyTheOwningProductionPathCallsEachRawMutation(string target, string method, string expectedCaller)
    {
        var assemblies = new[]
        {
            typeof(CreditService).Assembly, typeof(CreditAccount).Assembly, typeof(BandUpDbContext).Assembly,
            Assembly.Load("BandUp.Api"), Assembly.Load("BandUp.Worker")
        };
        var callers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var assembly in assemblies)
        {
            using var module = ModuleDefinition.ReadModule(assembly.Location);
            foreach (var type in module.GetTypes())
            {
                bool callsTarget = type.Methods.Any(m => m.HasBody && m.Body.Instructions.Any(i =>
                    i.Operand is MethodReference reference &&
                    reference.DeclaringType.Name == target && reference.Name == method));
                if (callsTarget)
                {
                    var caller = type;
                    // Async methods call through a compiler-generated nested state machine.
                    while (caller.DeclaringType is not null)
                    {
                        caller = caller.DeclaringType;
                    }
                    callers.Add(caller.Name);
                }
            }
        }
        Assert.Equal(expectedCaller.Split(','), callers.OrderBy(name => name, StringComparer.Ordinal).ToArray());
    }
}
