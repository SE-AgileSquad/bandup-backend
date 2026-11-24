using BandUp.Domain.Common;

namespace BandUp.Domain.Exams;

/// <summary>
/// One learner's sitting of one reading passage. The server owns the clock: the deadline is
/// fixed when the attempt starts, and every time-dependent rule takes "now" as a parameter so
/// the browser never supplies a time. The invariants: an attempt is submitted at most once,
/// answers are accepted only until the deadline plus a short grace, and grading happens only
/// inside the single transition from in-progress to submitted.
/// </summary>
public sealed class Attempt : Entity
{
    /// <summary>
    /// Extra time after the deadline in which a request that left the browser on time is still
    /// accepted, so network delay does not cost the learner their last answers.
    /// </summary>
    public static readonly TimeSpan SubmissionGrace = TimeSpan.FromSeconds(10);

    public const int MaxAnswerLength = 500;

    private readonly List<AttemptAnswer> _answers = [];

    private Attempt()
    {
    }

    public Guid UserId { get; private set; }

    public Guid PassageId { get; private set; }

    public AttemptStatus Status { get; private set; } = AttemptStatus.InProgress;

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? SubmittedAt { get; private set; }

    public int? RawScore { get; private set; }

    public int? TotalQuestions { get; private set; }

    /// <summary>
    /// The approximate IELTS band for the score, set once when the attempt is graded and null
    /// before that. See <see cref="BandEstimator"/>.
    /// </summary>
    public decimal? ApproxBand { get; private set; }

    public string? IdempotencyKey { get; private set; }

    public IReadOnlyList<AttemptAnswer> Answers => _answers.AsReadOnly();

    public static Attempt Start(Guid userId, Guid passageId, DateTimeOffset now, TimeSpan duration)
    {
        if (userId == Guid.Empty)
        {
            throw new DomainException("An attempt requires a user.");
        }

        if (passageId == Guid.Empty)
        {
            throw new DomainException("An attempt requires a passage.");
        }

        if (duration <= TimeSpan.Zero)
        {
            throw new DomainException("An attempt must have a positive duration.");
        }

        DateTimeOffset startedAt = AtDatabasePrecision(now);

        return new Attempt
        {
            UserId = userId,
            PassageId = passageId,
            StartedAt = startedAt,
            ExpiresAt = startedAt + duration,
        };
    }

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    public bool CanAcceptAnswers(DateTimeOffset now) =>
        Status == AttemptStatus.InProgress && now <= ExpiresAt + SubmissionGrace;

    /// <summary>
    /// The status as the clock sees it. An attempt still stored as in progress whose deadline and
    /// grace have passed is expired even if nothing has closed it yet, so a read never shows a
    /// finished sitting as running.
    /// </summary>
    public AttemptStatus EffectiveStatus(DateTimeOffset now) =>
        Status == AttemptStatus.InProgress && now > ExpiresAt + SubmissionGrace
            ? AttemptStatus.Expired
            : Status;

    /// <summary>
    /// Closes a sitting that ran out of time without being submitted. No score is recorded: the
    /// learner is told that time ran out and starts the passage again. Allowed only once the
    /// deadline and grace have passed; calling it again on an expired attempt does nothing.
    /// </summary>
    public void Expire(DateTimeOffset now)
    {
        if (Status == AttemptStatus.Submitted)
        {
            throw new DomainException("A submitted attempt cannot expire.");
        }

        if (Status == AttemptStatus.Expired)
        {
            return;
        }

        if (CanAcceptAnswers(now))
        {
            throw new DomainException("This attempt still has time left and cannot be closed as expired.");
        }

        Status = AttemptStatus.Expired;
    }

    /// <summary>
    /// Time left on the deadline as the server sees it; zero once submitted, expired or past the
    /// deadline.
    /// </summary>
    public TimeSpan RemainingAt(DateTimeOffset now)
    {
        if (Status != AttemptStatus.InProgress)
        {
            return TimeSpan.Zero;
        }

        TimeSpan remaining = ExpiresAt - now;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    /// <summary>
    /// True when this attempt was already submitted with the same idempotency key, so a retry
    /// can be answered with the stored result instead of an error.
    /// </summary>
    public bool IsReplayOf(string idempotencyKey) =>
        Status == AttemptStatus.Submitted && IdempotencyKey == idempotencyKey;

    /// <summary>
    /// Saves or replaces the answer to one question; a blank answer removes it.
    /// </summary>
    public void SaveAnswer(Guid questionId, string? selectedAnswer, DateTimeOffset now)
    {
        if (!CanAcceptAnswers(now))
        {
            throw new DomainException("This attempt no longer accepts answers.");
        }

        ApplyAnswer(questionId, selectedAnswer);
    }

    /// <summary>
    /// Closes the attempt and grades it against the answer keys, then records the approximate
    /// band for the score. The answers sent with the submission are applied first. It is allowed
    /// only while the attempt still accepts answers (the deadline plus grace): after that the
    /// sitting is expired and records no result. A repeat call throws, so an attempt can never be
    /// graded twice.
    /// </summary>
    public void Submit(
        IReadOnlyCollection<SubmittedAnswer> finalAnswers,
        IReadOnlyCollection<ScoringKey> keys,
        string idempotencyKey,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(finalAnswers);
        ArgumentNullException.ThrowIfNull(keys);

        if (Status == AttemptStatus.Submitted)
        {
            throw new DomainException("This attempt was already submitted.");
        }

        if (!CanAcceptAnswers(now))
        {
            throw new DomainException("Time ran out before this attempt was submitted, so it records no result.");
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new DomainException("A submission requires an idempotency key.");
        }

        Dictionary<Guid, ScoringKey> keysByQuestion = keys.ToDictionary(key => key.QuestionId);

        // Validate everything before changing anything, so a bad request leaves the attempt untouched.
        foreach (SubmittedAnswer answer in finalAnswers)
        {
            if (!keysByQuestion.ContainsKey(answer.QuestionId))
            {
                throw new DomainException($"Question {answer.QuestionId} does not belong to this attempt.");
            }

            if (answer.SelectedAnswer?.Trim().Length > MaxAnswerLength)
            {
                throw new DomainException($"An answer cannot be longer than {MaxAnswerLength} characters.");
            }
        }

        foreach (SubmittedAnswer answer in finalAnswers)
        {
            ApplyAnswer(answer.QuestionId, answer.SelectedAnswer);
        }

        int score = 0;

        foreach (AttemptAnswer answer in _answers)
        {
            bool isCorrect = keysByQuestion.TryGetValue(answer.QuestionId, out ScoringKey? key)
                && AnswerMatcher.IsCorrect(answer.SelectedAnswer, key);

            answer.Grade(isCorrect);

            if (isCorrect)
            {
                score++;
            }
        }

        Status = AttemptStatus.Submitted;
        SubmittedAt = AtDatabasePrecision(now);
        RawScore = score;
        TotalQuestions = keysByQuestion.Count;
        ApproxBand = BandEstimator.Estimate(score, keysByQuestion.Count);
        IdempotencyKey = idempotencyKey;
    }

    /// <summary>
    /// PostgreSQL keeps timestamps to the microsecond while a .NET clock reads to 100 ns. Cutting
    /// the extra digit here makes what is returned right after a write identical to what is read
    /// back later, which a replayed submit relies on to return a byte-identical result.
    /// </summary>
    private static DateTimeOffset AtDatabasePrecision(DateTimeOffset value) =>
        value.AddTicks(-(value.Ticks % (TimeSpan.TicksPerMillisecond / 1000)));

    private void ApplyAnswer(Guid questionId, string? selectedAnswer)
    {
        if (questionId == Guid.Empty)
        {
            throw new DomainException("An answer requires a question.");
        }

        AttemptAnswer? existing = _answers.Find(answer => answer.QuestionId == questionId);

        if (string.IsNullOrWhiteSpace(selectedAnswer))
        {
            if (existing is not null)
            {
                _answers.Remove(existing);
            }

            return;
        }

        string trimmed = selectedAnswer.Trim();

        if (trimmed.Length > MaxAnswerLength)
        {
            throw new DomainException($"An answer cannot be longer than {MaxAnswerLength} characters.");
        }

        if (existing is null)
        {
            _answers.Add(new AttemptAnswer(Id, questionId, trimmed));
            return;
        }

        existing.Change(trimmed);
    }
}
