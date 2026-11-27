using BandUp.Application.Abstractions;
using BandUp.Application.Features.Writing;
using BandUp.Application.Features.Writing.SubmitWritingEssay;
using BandUp.Domain.Writing;
using NSubstitute;
using Shouldly;

namespace BandUp.UnitTests.Features.Writing;

public sealed class WritingAcceptanceTests
{
    [Fact]
    public void StructuralValidationPrecedesReplayButWordCountIsForNewSubmissions()
    {
        var validator = new SubmitWritingEssayValidator();
        var command = new SubmitWritingEssayCommand(Guid.NewGuid(), Guid.NewGuid(), "short");
        validator.Validate(command).IsValid.ShouldBeTrue();
        validator.Validate(command with { IdempotencyKey = Guid.Empty }).IsValid.ShouldBeFalse();
        validator.Validate(command with { PromptId = Guid.Empty }).IsValid.ShouldBeFalse();
        foreach (var unsafeEssay in new[] { "x\0", "\ud800", new string('x', EssayPolicy.MaxScalars + 1) })
        {
            validator.Validate(command with { EssayText = unsafeEssay }).IsValid.ShouldBeFalse();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingKeyPrecedesCatalogWordCountAndCreditChecks(bool changed)
    {
        var owner = Guid.NewGuid();
        var now = DateTimeOffset.UnixEpoch;
        var prompt = WritingPrompt.CreateDraft("Cities", "Discuss growth.", "Opinion", 100, 350, owner, now);
        prompt.Publish(now);
        string essay = string.Join(" ", Enumerable.Repeat("word", 100));
        var saved = new WritingSubmission(owner, Guid.NewGuid(), prompt, essay, now, now.AddMinutes(5));
        var caller = Substitute.For<ICurrentUser>();
        caller.IsAuthenticated.Returns(true);
        caller.UserId.Returns(owner);
        var store = Substitute.For<IWritingSubmissionStore>();
        var credits = Substitute.For<ICreditService>();
        store.FindByKeyAsync(owner, saved.IdempotencyKey, CancellationToken.None).Returns(saved);
        var handler = new SubmitWritingEssayHandler(caller, store, credits, TimeProvider.System, new());
        var command = new SubmitWritingEssayCommand(saved.IdempotencyKey, prompt.Id, changed ? "short" : essay);
        var result = await handler.HandleAsync(command, CancellationToken.None);
        if (changed)
        {
            result.Error!.Code.ShouldBe(WritingErrorCodes.IdempotencyConflict);
        }
        else
        {
            result.Value.ShouldBe(new WritingSubmissionAcceptedDto(saved.Id, now));
        }
        credits.ReceivedCalls().ShouldBeEmpty();
        await store.DidNotReceiveWithAnyArgs().FindEligiblePromptAsync(default, default);
    }
}
