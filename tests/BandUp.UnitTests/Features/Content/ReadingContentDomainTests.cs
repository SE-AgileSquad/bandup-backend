using BandUp.Domain.Common;
using BandUp.Domain.Content;
using Shouldly;

namespace BandUp.UnitTests.Features.Content;

public sealed class ReadingContentDomainTests
{
    private static readonly Guid AdminId = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Publishing_Validates_All_Questions_Before_Changing_Any_Status()
    {
        Passage passage = CreatePassage();
        Question ready = AddVerifiedQuestion(passage);
        Question unverified = passage.AddQuestion(QuestionType.SentenceCompletion, "The birds build ___", 2);
        unverified.SetExplanation("The passage uses 'nests'.", "The birds build nests.");
        unverified.SetAnswerKey("nests");

        Should.Throw<DomainException>(() => passage.Publish(Now));
        passage.Status.ShouldBe(PassageStatus.Draft);
        passage.PublishedAt.ShouldBeNull();
        ready.Status.ShouldBe(QuestionStatus.Draft);

        unverified.VerifyAnswerKey(AdminId, Now);
        passage.Publish(Now);

        passage.Status.ShouldBe(PassageStatus.Published);
        passage.PublishedAt.ShouldBe(Now);
        passage.Questions.ShouldAllBe(question => question.Status == QuestionStatus.Published);
    }

    [Fact]
    public void Publishing_Requires_An_Explanation_And_Evidence()
    {
        Passage passage = CreatePassage();
        Question question = passage.AddQuestion(QuestionType.SentenceCompletion, "The birds build ___", 1);
        question.SetAnswerKey("nests");

        Should.Throw<DomainException>(() => question.VerifyAnswerKey(AdminId, Now));
        question.SetExplanation("The passage uses 'nests'.");
        Should.Throw<DomainException>(() => question.VerifyAnswerKey(AdminId, Now));
        Should.Throw<DomainException>(() => passage.Publish(Now));
    }

    [Fact]
    public void Not_Given_Uses_An_Unspecified_Part_In_Place_Of_Evidence()
    {
        Passage passage = CreatePassage();
        Question question = passage.AddQuestion(
            QuestionType.TrueFalseNotGiven,
            "The birds build nests twice a year.",
            1);
        question.SetAnswerKey(" not given ");
        question.SetExplanation("The passage gives no frequency.", "The birds build nests.");

        Should.Throw<DomainException>(() => question.VerifyAnswerKey(AdminId, Now));

        question.SetExplanation(
            "The passage gives no frequency.",
            misleadingSentence: "The birds build nests.",
            unspecifiedPart: "Twice a year.");
        question.VerifyAnswerKey(AdminId, Now);
        passage.Publish(Now);

        question.AnswerKey!.CorrectAnswer.ShouldBe("Not Given");
        question.EvidenceSentence.ShouldBeNull();
        question.Status.ShouldBe(QuestionStatus.Published);
    }

    [Fact]
    public void Answer_Keys_Validate_Tfng_Choices_And_Keep_Sentence_Completion_Variants()
    {
        Passage passage = CreatePassage();
        Question tfng = passage.AddQuestion(QuestionType.TrueFalseNotGiven, "The birds build nests.", 1);
        Question completion = passage.AddQuestion(QuestionType.SentenceCompletion, "The birds build ___", 2);

        Should.Throw<DomainException>(() => tfng.SetAnswerKey("Maybe"));
        Should.Throw<DomainException>(() => tfng.SetAnswerKey("True", ["Yes"]));

        string[] variants = [" their nests "];
        completion.SetAnswerKey("nests", variants);
        variants[0] = "another value";
        completion.AnswerKey!.AcceptedVariants.ShouldBe(["their nests"]);
    }

    [Fact]
    public void Changing_The_Answer_Prompt_Explanation_Or_Passage_Body_Invalidates_Verification()
    {
        Passage passage = CreatePassage();
        Question question = AddVerifiedQuestion(passage);
        AnswerKey key = question.AnswerKey!;

        question.SetAnswerKey("False");
        key.IsVerified.ShouldBeFalse();
        question.AnswerKey.ShouldBeSameAs(key);

        question.VerifyAnswerKey(AdminId, Now);
        question.EditDraft("The birds build nests in winter.", 1);
        key.IsVerified.ShouldBeFalse();

        question.VerifyAnswerKey(AdminId, Now);
        question.SetExplanation("The passage does not mention winter.", "The birds build nests.");
        key.IsVerified.ShouldBeFalse();

        question.VerifyAnswerKey(AdminId, Now);
        passage.EditDraft(passage.Title, "The birds build nests in spring.", passage.SourceNote, passage.UsageBasis);
        key.IsVerified.ShouldBeFalse();
    }

    [Fact]
    public void Revising_A_Published_Passage_Copies_Content_Into_An_Independent_Draft()
    {
        Passage passage = CreatePassage();
        Question originalQuestion = AddVerifiedQuestion(passage);
        passage.Publish(Now);

        Should.Throw<DomainException>(() =>
            passage.EditDraft("New title", passage.Body, passage.SourceNote, passage.UsageBasis));
        Should.Throw<DomainException>(() => originalQuestion.SetAnswerKey("False"));
        Should.Throw<DomainException>(() =>
            passage.AddQuestion(QuestionType.SentenceCompletion, "The birds build ___", 2));

        Passage revision = passage.CreateRevision(AdminId);
        Question copiedQuestion = revision.Questions.Single();
        AnswerKey copiedKey = copiedQuestion.AnswerKey!;

        revision.Id.ShouldNotBe(passage.Id);
        revision.PreviousVersionId.ShouldBe(passage.Id);
        revision.Version.ShouldBe(passage.Version + 1);
        revision.Status.ShouldBe(PassageStatus.Draft);
        revision.PublishedAt.ShouldBeNull();
        revision.Body.ShouldBe(passage.Body);
        copiedQuestion.Id.ShouldNotBe(originalQuestion.Id);
        copiedQuestion.PassageId.ShouldBe(revision.Id);
        copiedQuestion.Status.ShouldBe(QuestionStatus.Draft);
        copiedKey.Id.ShouldNotBe(originalQuestion.AnswerKey!.Id);
        copiedKey.QuestionId.ShouldBe(copiedQuestion.Id);
        copiedKey.IsVerified.ShouldBeFalse();
        Should.Throw<DomainException>(() => revision.Publish(Now));

        copiedQuestion.SetAnswerKey("False");
        originalQuestion.AnswerKey.CorrectAnswer.ShouldBe("True");
        passage.Archive();
        passage.Status.ShouldBe(PassageStatus.Archived);
        passage.PublishedAt.ShouldBe(Now);
    }

    [Fact]
    public void An_Incomplete_Explanation_Can_Be_Saved_But_All_Publication_Issues_Are_Reported()
    {
        Passage passage = CreatePassage();
        Question missingKey = passage.AddQuestion(QuestionType.SentenceCompletion, "Birds build ___", 1);
        Question incomplete = passage.AddQuestion(QuestionType.TrueFalseNotGiven, "All birds migrate.", 2);
        incomplete.SetAnswerKey("False");
        incomplete.SetExplanation(" ", evidenceSentence: "");

        IReadOnlyList<PublicationIssue> issues = passage.GetPublicationIssues();
        issues.ShouldContain(issue => issue.QuestionId == missingKey.Id && issue.Field == "answerKey.correctAnswer");
        issues.ShouldContain(issue => issue.QuestionId == incomplete.Id && issue.Field == "explanation");
        issues.ShouldContain(issue => issue.QuestionId == incomplete.Id && issue.Field == "evidenceSentence");
        issues.ShouldContain(issue => issue.QuestionId == incomplete.Id && issue.Field == "answerKey.isVerified");
        incomplete.Explanation.ShouldBeNull();
        Should.Throw<DomainException>(() => incomplete.VerifyAnswerKey(AdminId, Now));
        Should.Throw<DomainException>(() => passage.Publish(Now));
        passage.Questions.ShouldAllBe(question => question.Status == QuestionStatus.Draft);
    }

    [Fact]
    public void Saving_Identical_Answers_And_Explanations_Preserves_Verification()
    {
        Passage passage = CreatePassage();
        Question question = AddVerifiedQuestion(passage);
        question.SetAnswerKey(" true ");
        question.SetExplanation(question.Explanation, question.EvidenceSentence);
        question.AnswerKey!.IsVerified.ShouldBeTrue();
        question.AnswerKey.VerifiedAt.ShouldBe(Now);

        question.ClearAnswerKeyVerification();
        question.AnswerKey.IsVerified.ShouldBeFalse();
        question.VerifyAnswerKey(AdminId, Now);
        question.SetExplanation(null, question.EvidenceSentence);
        question.AnswerKey.IsVerified.ShouldBeFalse();

        question.ClearAnswerKey();
        question.AnswerKey.ShouldBeNull();
        question.GetPublicationIssues().ShouldContain(issue => issue.Field == "answerKey.correctAnswer");
    }

    private static Passage CreatePassage() =>
        Passage.CreateDraft("Birds", "The birds build nests.", "BandUp team", "Original content", AdminId);

    private static Question AddVerifiedQuestion(Passage passage)
    {
        Question question = passage.AddQuestion(QuestionType.TrueFalseNotGiven, "The birds build nests.", 1);
        question.SetExplanation("This is stated directly.", "The birds build nests.");
        question.SetAnswerKey("True");
        question.VerifyAnswerKey(AdminId, Now);
        return question;
    }
}
