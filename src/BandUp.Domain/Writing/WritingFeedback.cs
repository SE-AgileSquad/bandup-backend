using BandUp.Domain.Common;

namespace BandUp.Domain.Writing;

public sealed class WritingFeedback : Entity
{
    public const string Version = "1.0";

    // Shared primary key: feedback identity is its submission identity.
    public WritingFeedback(Guid submissionId, string summary, CriterionFeedback tr, CriterionFeedback cc,
        CriterionFeedback lr, CriterionFeedback gra) : base(submissionId)
    {
        if (submissionId == Guid.Empty)
        {
            throw new DomainException("A submission identity is required.");
        }
        Content = new WritingFeedbackContent(summary, tr, cc, lr, gra);
        EstimatedOverallBand = BandScores.Overall(tr.Score, cc.Score, lr.Score, gra.Score);
    }

    private WritingFeedback() { }

    public WritingFeedbackContent Content { get; private set; } = null!;
    public string SchemaVersion { get; private set; } = Version;
    public string Summary => Content.Summary;
    public CriterionFeedback Tr => Content.Tr;
    public CriterionFeedback Cc => Content.Cc;
    public CriterionFeedback Lr => Content.Lr;
    public CriterionFeedback Gra => Content.Gra;
    public decimal EstimatedOverallBand { get; private set; }

    /// <summary>Evidence quotes refer to the original essay, including its exact Unicode representation.</summary>
    public bool HasEvidenceIn(string essay) =>
        new[] { Tr, Cc, Lr, Gra }.All(c => c.Evidence is null ||
            c.Evidence.All(e => essay.Contains(e.Excerpt, StringComparison.Ordinal)));

    /// <summary>Compare the immutable feedback document, including the order of its lists, on terminal replay.</summary>
    public bool MatchesFeedback(WritingFeedback? other) =>
        other is not null && Id == other.Id && SchemaVersion == other.SchemaVersion &&
        EstimatedOverallBand == other.EstimatedOverallBand && Summary == other.Summary &&
        Same(Tr, other.Tr) && Same(Cc, other.Cc) && Same(Lr, other.Lr) && Same(Gra, other.Gra);

    private static bool Same(CriterionFeedback left, CriterionFeedback right) =>
        left.Score == right.Score && left.Explanation == right.Explanation &&
        left.Strengths.SequenceEqual(right.Strengths) &&
        left.AreasForImprovement.SequenceEqual(right.AreasForImprovement) &&
        left.Recommendations.SequenceEqual(right.Recommendations) &&
        (left.Evidence is null ? right.Evidence is null : right.Evidence is not null &&
            left.Evidence.Select(e => (e.Excerpt, e.Observation))
                .SequenceEqual(right.Evidence.Select(e => (e.Excerpt, e.Observation))));
}

/// <summary>Immutable validated content stored as a single versioned feedback document.</summary>
public sealed class WritingFeedbackContent(string summary, CriterionFeedback tr, CriterionFeedback cc,
    CriterionFeedback lr, CriterionFeedback gra)
{
    public string Summary { get; } = CriterionFeedback.RequireText(summary);
    public CriterionFeedback Tr { get; } = tr ?? throw new DomainException("TR is required.");
    public CriterionFeedback Cc { get; } = cc ?? throw new DomainException("CC is required.");
    public CriterionFeedback Lr { get; } = lr ?? throw new DomainException("LR is required.");
    public CriterionFeedback Gra { get; } = gra ?? throw new DomainException("GRA is required.");
}

public static class BandScores
{
    public static bool IsValid(decimal score) => score is >= 0 and <= 9 && score % 0.5m == 0;

    public static decimal Overall(decimal tr, decimal cc, decimal lr, decimal gra)
    {
        if (!IsValid(tr) || !IsValid(cc) || !IsValid(lr) || !IsValid(gra))
        {
            throw new ArgumentOutOfRangeException(nameof(tr), "Expected four scores in half-band increments from zero to nine.");
        }
        return Math.Round((tr + cc + lr + gra) / 2m, MidpointRounding.AwayFromZero) / 2m;
    }
}
