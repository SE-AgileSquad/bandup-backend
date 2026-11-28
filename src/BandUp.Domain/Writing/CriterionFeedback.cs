using BandUp.Domain.Common;

namespace BandUp.Domain.Writing;

public sealed class CriterionFeedback
{
    public CriterionFeedback(decimal score, string explanation, IReadOnlyList<string> strengths,
        IReadOnlyList<string> areasForImprovement, IReadOnlyList<string> recommendations,
        IReadOnlyList<WritingEvidence>? evidence = null)
    {
        if (!BandScores.IsValid(score))
        {
            throw new DomainException("Invalid criterion score.");
        }
        Score = score;
        Explanation = RequireText(explanation);
        Strengths = CopyList(strengths);
        AreasForImprovement = CopyList(areasForImprovement);
        Recommendations = CopyList(recommendations, minimum: 1);
        if (evidence is not null)
        {
            if (evidence.Count is < 1 or > 3 || evidence.Any(item => item is null))
            {
                throw new DomainException("Invalid evidence list.");
            }
            Evidence = Array.AsReadOnly(evidence.ToArray());
        }
    }

    public decimal Score { get; }
    public string Explanation { get; }
    public IReadOnlyList<string> Strengths { get; }
    public IReadOnlyList<string> AreasForImprovement { get; }
    public IReadOnlyList<string> Recommendations { get; }
    public IReadOnlyList<WritingEvidence>? Evidence { get; }

    internal static string RequireText(string text, int maximum = 1000)
    {
        if (string.IsNullOrWhiteSpace(text) || !EssayPolicy.FitsScalarLimit(text, maximum))
        {
            throw new DomainException("Invalid feedback text.");
        }
        return text;
    }

    private static IReadOnlyList<string> CopyList(IReadOnlyList<string> values, int minimum = 0)
    {
        if (values is null || values.Count < minimum || values.Count > 5)
        {
            throw new DomainException("Invalid feedback list.");
        }
        return Array.AsReadOnly(values.Select(value => RequireText(value)).ToArray());
    }
}

public sealed class WritingEvidence(string excerpt, string observation)
{
    public string Excerpt { get; } = CriterionFeedback.RequireText(excerpt, 240);
    public string Observation { get; } = CriterionFeedback.RequireText(observation);
}
