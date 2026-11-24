using BandUp.Application.Common;

namespace BandUp.Application.Features.Exams;

/// <summary>
/// Expected failures of the exam flow, each with a stable code the frontend can switch on.
/// </summary>
public static class ExamErrors
{
    public const string AttemptNotFoundCode = "ATTEMPT_NOT_FOUND";
    public const string AttemptExpiredCode = "ATTEMPT_EXPIRED";
    public const string AttemptAlreadySubmittedCode = "ATTEMPT_ALREADY_SUBMITTED";
    public const string AttemptNotSubmittedCode = "ATTEMPT_NOT_SUBMITTED";
    public const string IdempotencyKeyReusedCode = "IDEMPOTENCY_KEY_REUSED";

    /// <summary>
    /// Also returned for an attempt that belongs to someone else, so ids cannot be probed.
    /// </summary>
    public static Error AttemptNotFound() =>
        new(AttemptNotFoundCode, "The attempt does not exist.", ErrorType.NotFound);

    public static Error AttemptExpired() =>
        new(
            AttemptExpiredCode,
            "Time ran out before this test was submitted, so no result was recorded.",
            ErrorType.Conflict);

    public static Error AttemptAlreadySubmitted() =>
        new(AttemptAlreadySubmittedCode, "This attempt was already submitted.", ErrorType.Conflict);

    public static Error AttemptNotSubmitted() =>
        new(AttemptNotSubmittedCode, "The result is available once the attempt is submitted.", ErrorType.Conflict);

    public static Error IdempotencyKeyReused() =>
        new(
            IdempotencyKeyReusedCode,
            "This idempotency key was already used for a different attempt.",
            ErrorType.Conflict);

    public static Error QuestionNotInAttempt() =>
        Error.Validation(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["questionId"] = ["The question does not belong to this attempt."],
        });
}
