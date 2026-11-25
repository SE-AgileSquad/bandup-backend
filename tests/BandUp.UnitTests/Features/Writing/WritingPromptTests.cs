using BandUp.Domain.Common;
using BandUp.Domain.Writing;
using Shouldly;

namespace BandUp.UnitTests.Features.Writing;

public sealed class WritingPromptTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, 350)]
    [InlineData(-1, 350)]
    [InlineData(350, 100)]
    public void Invalid_Word_Ranges_Do_Not_Change_A_Draft(int min, int max)
    {
        WritingPrompt prompt = Create();
        Should.Throw<DomainException>(() => prompt.EditDraft("Changed", "Changed", "Discussion", min, max, Now.AddHours(1)));
        prompt.Title.ShouldBe("Remote work");
        prompt.UpdatedAt.ShouldBe(Now);
    }

    [Fact]
    public void Published_Content_Is_Immutable_And_A_Copy_Is_An_Independent_Draft()
    {
        WritingPrompt prompt = Create();
        prompt.EditDraft(" Remote work ", " Discuss remote work. ", " Opinion essay ", 100, 350, Now.AddMinutes(1));
        prompt.UpdatedAt.ShouldBe(Now);
        prompt.Publish(Now.AddMinutes(2));
        prompt.Publish(Now.AddMinutes(3));
        prompt.UpdatedAt.ShouldBe(Now.AddMinutes(2));
        Should.Throw<DomainException>(() => prompt.EditDraft("Changed", "Text", "Opinion", 100, 350, Now));
        WritingPrompt copy = prompt.Duplicate(Actor, Now.AddHours(1));
        copy.Id.ShouldNotBe(prompt.Id);
        copy.Status.ShouldBe(WritingPromptStatus.Draft);
        copy.PublishedAt.ShouldBeNull();
        copy.TaskText.ShouldBe(prompt.TaskText);
        copy.CreatedAt.ShouldBe(Now.AddHours(1));
        prompt.Archive(Now.AddHours(2));
        prompt.Archive(Now.AddHours(3));
        prompt.UpdatedAt.ShouldBe(Now.AddHours(2));
        prompt.PublishedAt.ShouldBe(Now.AddMinutes(2));
        Should.Throw<DomainException>(() => prompt.Publish(Now));
    }

    [Fact]
    public void Drafts_Cannot_Be_Archived_And_Require_Content_And_A_Creator()
    {
        Should.Throw<DomainException>(() => Create().Archive(Now));
        Should.Throw<DomainException>(() => WritingPrompt.CreateDraft("Title", "Text", "Opinion", 100, 350, Guid.Empty, Now));
        Should.Throw<DomainException>(() => WritingPrompt.CreateDraft("Title", " ", "Opinion", 100, 350, Actor, Now));
    }

    private static WritingPrompt Create() =>
        WritingPrompt.CreateDraft("Remote work", "Discuss remote work.", "Opinion essay", 100, 350, Actor, Now);
}
