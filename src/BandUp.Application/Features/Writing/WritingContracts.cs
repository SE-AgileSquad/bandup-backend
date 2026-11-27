using System.Text.Json.Serialization;

namespace BandUp.Application.Features.Writing;

public sealed record SubmitWritingEssayRequest(Guid PromptId, string EssayText);

public static class WritingErrorCodes
{
    public const string InsufficientCredit = "INSUFFICIENT_CREDIT";
    public const string IdempotencyConflict = "IDEMPOTENCY_CONFLICT";
    public const string EvaluationNotReady = "EVALUATION_NOT_READY";
    public const string EvaluationFailed = "EVALUATION_FAILED";
}

public sealed record WritingPromptDto(Guid Id, string Title, string PromptText);

public sealed record WritingPromptListDto(IReadOnlyList<WritingPromptDto> Items);

public sealed record WritingSubmissionDetailDto(Guid SubmissionId, string EssayText, WritingPromptDto PromptSnapshot);

public sealed record WritingSubmissionHistoryItemDto(Guid SubmissionId, Guid PromptId, string Title,
    WritingEvaluationStatus Status, DateTimeOffset SubmittedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? CompletedAt = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? EstimatedOverallBand = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ScoreLabel => EstimatedOverallBand.HasValue ? WritingFeedbackDto.EstimatedLabel : null;
}

public enum WritingEvaluationStatus { Queued, Processing, Succeeded, Failed }

public sealed record WritingSubmissionAcceptedDto(Guid SubmissionId, DateTimeOffset SubmittedAt)
{
    public WritingEvaluationStatus Status => WritingEvaluationStatus.Queued;
}

public sealed record WritingEvaluationFailureDto(string Code)
{
    public bool CreditRefunded => true;
}

public sealed record WritingEvaluationStatusDto(Guid SubmissionId, WritingEvaluationStatus Status, DateTimeOffset SubmittedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? CompletedAt = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ResultUrl = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WritingEvaluationFailureDto? Failure = null);

public sealed record WritingEvaluationResultDto(Guid SubmissionId, DateTimeOffset CompletedAt, WritingFeedbackDto Feedback);

public sealed record WritingFeedbackDto(string Summary, WritingCriteriaDto Criteria)
{
    public const string EstimatedLabel = "estimated";

    public string SchemaVersion => "1.0";
    public string ScoreLabel => EstimatedLabel;
    public decimal EstimatedOverallBand => Criteria.EstimatedBand();
}

public sealed record WritingCriteriaDto(WritingCriterionFeedbackDto Tr, WritingCriterionFeedbackDto Cc,
    WritingCriterionFeedbackDto Lr, WritingCriterionFeedbackDto Gra)
{
    public decimal EstimatedBand()
    {
        static decimal CheckedScore(decimal score, string criterion)
        {
            if (score is < 0m or > 9m || score % 0.5m != 0m)
            {
                throw new ArgumentOutOfRangeException(criterion, score, "Expected 0–9 in half-band increments.");
            }
            return score;
        }

        decimal mean = (CheckedScore(Tr.Score, nameof(Tr)) + CheckedScore(Cc.Score, nameof(Cc))
            + CheckedScore(Lr.Score, nameof(Lr)) + CheckedScore(Gra.Score, nameof(Gra))) / 4m;
        return Math.Round(mean * 2m, MidpointRounding.AwayFromZero) / 2m;
    }
}

public sealed record WritingCriterionFeedbackDto(decimal Score, string Explanation, IReadOnlyList<string> Strengths,
    IReadOnlyList<string> AreasForImprovement, IReadOnlyList<string> Recommendations,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<WritingEvidenceDto>? Evidence = null);

public sealed record WritingEvidenceDto(string Excerpt, string Observation);
