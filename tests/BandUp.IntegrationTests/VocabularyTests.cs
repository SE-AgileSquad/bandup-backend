using System.Net;
using System.Net.Http.Json;
using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Identity;
using BandUp.Application.Features.Vocabulary.ListDueReviews;
using BandUp.Application.Features.Vocabulary.RecordVocabularyReview;
using BandUp.Application.Features.Vocabulary.SaveVocabCard;
using BandUp.Domain.Content;
using BandUp.Domain.Identity;
using BandUp.Infrastructure.Persistence;
using BandUp.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BandUp.IntegrationTests;

[Collection(BandUpCollection.Name)]
public sealed class VocabularyTests(BandUpTestEnvironment environment)
{
    [Fact]
    public async Task Saving_And_Reviewing_A_Word_Uses_The_Owners_Queue_And_The_Server_Clock()
    {
        ReviewClock clock = new();
        User owner = CreateUser();
        User other = CreateUser();
        await using (BandUpDbContext db = BandUpTestEnvironment.CreateContext(environment.MigratedConnectionString))
        {
            db.Users.AddRange(owner, other);
            await db.SaveChangesAsync();
        }

        await using BandUpApiFactory factory = CreateFactory(clock);
        using HttpClient client = CreateClient(factory, owner);
        using HttpClient otherClient = CreateClient(factory, other);
        using HttpClient anonymousClient = factory.CreateClient();

        using HttpResponseMessage anonymous = await anonymousClient.GetAsync("/api/vocabulary/reviews/due");
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using HttpResponseMessage saved = await client.PostAsJsonAsync("/api/vocabulary/cards",
            new { word = " nest ", contextSentence = "The birds build nests." });
        saved.StatusCode.ShouldBe(HttpStatusCode.Created, await saved.Content.ReadAsStringAsync());
        SavedVocabCardDto card = (await saved.Content.ReadFromJsonAsync<SavedVocabCardDto>())!;
        card.DueDate.ShouldBe(new DateOnly(2026, 11, 14));
        string reviewPath = $"/api/vocabulary/reviews/{card.ReviewId}/complete";

        using HttpResponseMessage early = await client.PostAsJsonAsync(reviewPath, new { isCorrect = true });
        early.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using HttpResponseMessage foreign = await otherClient.PostAsJsonAsync(reviewPath, new { isCorrect = true });
        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        clock.AdvanceDays(1);
        (await ReadDueAsync(otherClient)).Items.ShouldBeEmpty();
        DueVocabularyReviewDto due = (await ReadDueAsync(client)).Items.Single();
        due.Id.ShouldBe(card.ReviewId);
        due.VocabCardId.ShouldBe(card.Id);
        due.Word.ShouldBe("nest");

        Task<HttpResponseMessage> firstReview = client.PostAsJsonAsync(reviewPath, new { isCorrect = true });
        Task<HttpResponseMessage> secondReview = client.PostAsJsonAsync(reviewPath, new { isCorrect = true });
        await Task.WhenAll(firstReview, secondReview);
        using HttpResponseMessage firstResult = await firstReview;
        using HttpResponseMessage secondResult = await secondReview;
        HttpResponseMessage accepted = firstResult.IsSuccessStatusCode ? firstResult : secondResult;
        HttpResponseMessage rejected = firstResult.IsSuccessStatusCode ? secondResult : firstResult;
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
        rejected.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        VocabularyReviewDto updated = (await accepted.Content.ReadFromJsonAsync<VocabularyReviewDto>())!;
        updated.LeitnerBox.ShouldBe(2);
        updated.DueDate.ShouldBe(new DateOnly(2026, 11, 16));
        (await ReadDueAsync(client)).Items.ShouldBeEmpty();

        clock.AdvanceDays(2);
        using HttpResponseMessage incorrect = await client.PostAsJsonAsync(reviewPath, new { isCorrect = false });
        incorrect.StatusCode.ShouldBe(HttpStatusCode.OK);
        VocabularyReviewDto reset = (await incorrect.Content.ReadFromJsonAsync<VocabularyReviewDto>())!;
        reset.LeitnerBox.ShouldBe(1);
        reset.StreakCorrect.ShouldBe(0);
        reset.DueDate.ShouldBe(new DateOnly(2026, 11, 17));
    }

    [Fact]
    public async Task A_Card_Can_Link_To_A_Published_Question_And_Concurrent_Duplicates_Are_Rejected()
    {
        ReviewClock clock = new();
        User owner = CreateUser();
        Passage published = Passage.CreateDraft("Birds", "The birds build nests.", "BandUp", "Original", owner.Id);
        Question question = published.AddQuestion(QuestionType.SentenceCompletion, "The birds build ___.", 1);
        question.SetExplanation("The birds build nests.", "The birds build nests.");
        question.SetAnswerKey("nests");
        question.VerifyAnswerKey(owner.Id, clock.GetUtcNow());
        published.Publish(clock.GetUtcNow());
        Passage draft = Passage.CreateDraft("Draft", "Draft body", "BandUp", "Original", owner.Id);
        Question draftQuestion = draft.AddQuestion(QuestionType.SentenceCompletion, "Draft ___.", 1);
        await using (BandUpDbContext db = BandUpTestEnvironment.CreateContext(environment.MigratedConnectionString))
        {
            db.Users.Add(owner);
            db.Passages.AddRange(published, draft);
            await db.SaveChangesAsync();
        }

        await using BandUpApiFactory factory = CreateFactory(clock);
        using HttpClient client = CreateClient(factory, owner);
        const string cardsPath = "/api/vocabulary/cards";
        using HttpResponseMessage draftSource = await client.PostAsJsonAsync(cardsPath,
            new { word = "nest", contextSentence = published.Body, sourceQuestionId = draftQuestion.Id });
        draftSource.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        object input = new { word = "nest", contextSentence = published.Body, sourceQuestionId = question.Id };
        Task<HttpResponseMessage> first = client.PostAsJsonAsync(cardsPath, input);
        Task<HttpResponseMessage> second = client.PostAsJsonAsync(cardsPath, input);
        await Task.WhenAll(first, second);
        using HttpResponseMessage firstResult = await first;
        using HttpResponseMessage secondResult = await second;
        HttpStatusCode[] statuses = [firstResult.StatusCode, secondResult.StatusCode];
        statuses.Order().ShouldBe(new[] { HttpStatusCode.Created, HttpStatusCode.Conflict });

        clock.AdvanceDays(1);
        (await ReadDueAsync(client)).Items.ShouldHaveSingleItem();
    }

    private BandUpApiFactory CreateFactory(ReviewClock clock) =>
        new(environment, configureServices: services => services.AddSingleton<TimeProvider>(clock));

    private static User CreateUser() =>
        User.Create($"{Guid.NewGuid():N}@example.test", "Learner", "test-hash", DateTimeOffset.UtcNow);

    private static HttpClient CreateClient(BandUpApiFactory factory, User user)
    {
        HttpClient client = factory.CreateClient();
        AccessToken token = factory.Services.GetRequiredService<ITokenService>()
            .CreateAccessToken(user.Id, nameof(Role.User), DateTimeOffset.UtcNow);
        client.DefaultRequestHeaders.Add("Cookie", $"bu_at={token.Value}");
        client.DefaultRequestHeaders.Add("X-Requested-With", "bandup");
        return client;
    }

    private static async Task<PagedResult<DueVocabularyReviewDto>> ReadDueAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<PagedResult<DueVocabularyReviewDto>>("/api/vocabulary/reviews/due"))!;

    private sealed class ReviewClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 13, 23, 55, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void AdvanceDays(int days) => _now = _now.AddDays(days);
    }
}
