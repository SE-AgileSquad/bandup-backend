using BandUp.Domain.Common;
using BandUp.Domain.Content;
using BandUp.Domain.Exams;
using Shouldly;

namespace BandUp.UnitTests.Features.Exams;

/// <summary>
/// The exam rules that live in the aggregate: a deadline set by the server, answers accepted
/// only until the deadline plus grace, and a single transition to submitted that grades once.
/// </summary>
public sealed class AttemptDomainTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 12, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Duration = TimeSpan.FromMinutes(20);

    private readonly Guid _userId = Guid.CreateVersion7();
    private readonly Guid _passageId = Guid.CreateVersion7();
    private readonly ScoringKey _first = new(Guid.CreateVersion7(), QuestionType.TrueFalseNotGiven, "True", []);
    private readonly ScoringKey _second =
        new(Guid.CreateVersion7(), QuestionType.SentenceCompletion, "nests", ["a nest"]);
    private readonly ScoringKey _third = new(Guid.CreateVersion7(), QuestionType.TrueFalseNotGiven, "Not Given", []);

    private ScoringKey[] Keys => [_first, _second, _third];

    private Attempt NewAttempt() => Attempt.Start(_userId, _passageId, Start, Duration);

    [Fact]
    public void Start_Fixes_The_Deadline_From_The_Given_Clock()
    {
        Attempt attempt = NewAttempt();

        attempt.Status.ShouldBe(AttemptStatus.InProgress);
        attempt.UserId.ShouldBe(_userId);
        attempt.PassageId.ShouldBe(_passageId);
        attempt.StartedAt.ShouldBe(Start);
        attempt.ExpiresAt.ShouldBe(Start + Duration);
        attempt.SubmittedAt.ShouldBeNull();
        attempt.RawScore.ShouldBeNull();
        attempt.Answers.ShouldBeEmpty();
    }

    [Fact]
    public void Start_Rejects_Missing_Ids_And_A_Non_Positive_Duration()
    {
        Should.Throw<DomainException>(() => Attempt.Start(Guid.Empty, _passageId, Start, Duration));
        Should.Throw<DomainException>(() => Attempt.Start(_userId, Guid.Empty, Start, Duration));
        Should.Throw<DomainException>(() => Attempt.Start(_userId, _passageId, Start, TimeSpan.Zero));
        Should.Throw<DomainException>(() => Attempt.Start(_userId, _passageId, Start, TimeSpan.FromMinutes(-1)));
    }

    [Fact]
    public void Timestamps_Are_Kept_At_Microsecond_Precision_Like_The_Database()
    {
        DateTimeOffset withExtraDigit = Start.AddTicks(7);
        Attempt attempt = Attempt.Start(_userId, _passageId, withExtraDigit, Duration);

        attempt.StartedAt.ShouldBe(Start);
        attempt.ExpiresAt.ShouldBe(Start + Duration);

        attempt.Submit([], Keys, "key-1", Start.AddMinutes(3).AddTicks(9));

        attempt.SubmittedAt.ShouldBe(Start.AddMinutes(3));
    }

    [Fact]
    public void RemainingAt_Counts_Down_And_Stops_At_Zero()
    {
        Attempt attempt = NewAttempt();

        attempt.RemainingAt(Start).ShouldBe(Duration);
        attempt.RemainingAt(Start.AddMinutes(5)).ShouldBe(TimeSpan.FromMinutes(15));
        attempt.RemainingAt(Start + Duration).ShouldBe(TimeSpan.Zero);
        attempt.RemainingAt(Start.AddHours(2)).ShouldBe(TimeSpan.Zero);
        attempt.IsExpired(Start.AddMinutes(19)).ShouldBeFalse();
        attempt.IsExpired(Start + Duration).ShouldBeTrue();
    }

    [Fact]
    public void SaveAnswer_Replaces_The_Previous_Answer_For_The_Same_Question()
    {
        Attempt attempt = NewAttempt();

        attempt.SaveAnswer(_first.QuestionId, "False", Start.AddMinutes(1));
        attempt.SaveAnswer(_first.QuestionId, "  True ", Start.AddMinutes(2));

        attempt.Answers.Count.ShouldBe(1);
        attempt.Answers[0].SelectedAnswer.ShouldBe("True");
        attempt.Answers[0].IsCorrect.ShouldBeNull();
    }

    [Fact]
    public void SaveAnswer_With_A_Blank_Value_Removes_The_Answer()
    {
        Attempt attempt = NewAttempt();
        attempt.SaveAnswer(_first.QuestionId, "True", Start.AddMinutes(1));

        attempt.SaveAnswer(_first.QuestionId, "  ", Start.AddMinutes(2));

        attempt.Answers.ShouldBeEmpty();
    }

    [Fact]
    public void SaveAnswer_Rejects_An_Overlong_Answer()
    {
        Attempt attempt = NewAttempt();

        Should.Throw<DomainException>(() =>
            attempt.SaveAnswer(_second.QuestionId, new string('a', Attempt.MaxAnswerLength + 1), Start));
    }

    [Fact]
    public void SaveAnswer_Is_Accepted_Within_Grace_And_Refused_After_It()
    {
        Attempt attempt = NewAttempt();
        DateTimeOffset deadline = Start + Duration;

        attempt.SaveAnswer(_first.QuestionId, "True", deadline + Attempt.SubmissionGrace);
        attempt.Answers.Count.ShouldBe(1);

        DateTimeOffset tooLate = deadline + Attempt.SubmissionGrace + TimeSpan.FromSeconds(1);

        Should.Throw<DomainException>(() => attempt.SaveAnswer(_second.QuestionId, "nests", tooLate));
        attempt.Answers.Count.ShouldBe(1);
    }

    [Fact]
    public void Submit_Grades_Every_Answer_And_Closes_The_Attempt()
    {
        Attempt attempt = NewAttempt();
        attempt.SaveAnswer(_first.QuestionId, "true", Start.AddMinutes(1));
        DateTimeOffset submitAt = Start.AddMinutes(12);

        attempt.Submit(
            [new SubmittedAnswer(_second.QuestionId, "A  Nest"), new SubmittedAnswer(_third.QuestionId, "True")],
            Keys,
            "key-1",
            submitAt);

        attempt.Status.ShouldBe(AttemptStatus.Submitted);
        attempt.SubmittedAt.ShouldBe(submitAt);
        attempt.RawScore.ShouldBe(2);
        attempt.TotalQuestions.ShouldBe(3);
        attempt.IdempotencyKey.ShouldBe("key-1");
        attempt.Answers.Single(answer => answer.QuestionId == _first.QuestionId).IsCorrect.ShouldBe(true);
        attempt.Answers.Single(answer => answer.QuestionId == _second.QuestionId).IsCorrect.ShouldBe(true);
        attempt.Answers.Single(answer => answer.QuestionId == _third.QuestionId).IsCorrect.ShouldBe(false);
    }

    [Fact]
    public void Submit_Counts_Unanswered_Questions_In_The_Total_But_Not_In_The_Score()
    {
        Attempt attempt = NewAttempt();

        attempt.Submit([], Keys, "key-1", Start.AddMinutes(3));

        attempt.RawScore.ShouldBe(0);
        attempt.TotalQuestions.ShouldBe(3);
        attempt.Answers.ShouldBeEmpty();
    }

    [Fact]
    public void Submit_Twice_Throws_And_Keeps_The_First_Result()
    {
        Attempt attempt = NewAttempt();
        attempt.Submit([new SubmittedAnswer(_first.QuestionId, "True")], Keys, "key-1", Start.AddMinutes(3));

        Should.Throw<DomainException>(() =>
            attempt.Submit([new SubmittedAnswer(_first.QuestionId, "False")], Keys, "key-2", Start.AddMinutes(4)));

        attempt.RawScore.ShouldBe(1);
        attempt.IdempotencyKey.ShouldBe("key-1");
        attempt.SubmittedAt.ShouldBe(Start.AddMinutes(3));
    }

    [Fact]
    public void IsReplayOf_Is_True_Only_For_The_Key_That_Submitted()
    {
        Attempt attempt = NewAttempt();
        attempt.IsReplayOf("key-1").ShouldBeFalse();

        attempt.Submit([], Keys, "key-1", Start.AddMinutes(3));

        attempt.IsReplayOf("key-1").ShouldBeTrue();
        attempt.IsReplayOf("key-2").ShouldBeFalse();
    }

    [Fact]
    public void Submit_After_Grace_Ignores_The_Final_Answers_And_Records_The_Deadline()
    {
        Attempt attempt = NewAttempt();
        attempt.SaveAnswer(_first.QuestionId, "True", Start.AddMinutes(10));
        DateTimeOffset late = Start + Duration + Attempt.SubmissionGrace + TimeSpan.FromSeconds(1);

        attempt.Submit([new SubmittedAnswer(_second.QuestionId, "nests")], Keys, "key-1", late);

        attempt.Status.ShouldBe(AttemptStatus.Submitted);
        attempt.SubmittedAt.ShouldBe(Start + Duration);
        attempt.RawScore.ShouldBe(1);
        attempt.Answers.Count.ShouldBe(1);
    }

    [Fact]
    public void Submit_Within_Grace_Still_Counts_The_Final_Answers()
    {
        Attempt attempt = NewAttempt();
        DateTimeOffset justLate = Start + Duration + TimeSpan.FromSeconds(5);

        attempt.Submit([new SubmittedAnswer(_second.QuestionId, "nests")], Keys, "key-1", justLate);

        attempt.RawScore.ShouldBe(1);
        attempt.SubmittedAt.ShouldBe(justLate);
    }

    [Fact]
    public void Submit_With_An_Unknown_Question_Throws_And_Changes_Nothing()
    {
        Attempt attempt = NewAttempt();
        attempt.SaveAnswer(_first.QuestionId, "True", Start.AddMinutes(1));

        Should.Throw<DomainException>(() => attempt.Submit(
            [new SubmittedAnswer(_second.QuestionId, "nests"), new SubmittedAnswer(Guid.CreateVersion7(), "x")],
            Keys,
            "key-1",
            Start.AddMinutes(3)));

        attempt.Status.ShouldBe(AttemptStatus.InProgress);
        attempt.Answers.Count.ShouldBe(1);
        attempt.Answers[0].IsCorrect.ShouldBeNull();
    }

    [Fact]
    public void Submit_Requires_An_Idempotency_Key()
    {
        Attempt attempt = NewAttempt();

        Should.Throw<DomainException>(() => attempt.Submit([], Keys, " ", Start.AddMinutes(1)));
        attempt.Status.ShouldBe(AttemptStatus.InProgress);
    }

    [Fact]
    public void A_Submitted_Attempt_Has_No_Time_Left_And_Accepts_No_Answers()
    {
        Attempt attempt = NewAttempt();
        attempt.Submit([], Keys, "key-1", Start.AddMinutes(3));

        attempt.RemainingAt(Start.AddMinutes(4)).ShouldBe(TimeSpan.Zero);
        attempt.CanAcceptAnswers(Start.AddMinutes(4)).ShouldBeFalse();
        Should.Throw<DomainException>(() => attempt.SaveAnswer(_first.QuestionId, "True", Start.AddMinutes(4)));
    }
}
