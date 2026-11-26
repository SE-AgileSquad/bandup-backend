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
        Summary = CriterionFeedback.RequireText(summary);
        Tr = tr ?? throw new DomainException("TR is required.");
        Cc = cc ?? throw new DomainException("CC is required.");
        Lr = lr ?? throw new DomainException("LR is required.");
        Gra = gra ?? throw new DomainException("GRA is required.");
    }

    public string SchemaVersion => Version;
    public string Summary { get; }
    public CriterionFeedback Tr { get; }
    public CriterionFeedback Cc { get; }
    public CriterionFeedback Lr { get; }
    public CriterionFeedback Gra { get; }
    public decimal EstimatedOverallBand => BandScores.Overall(Tr.Score, Cc.Score, Lr.Score, Gra.Score);
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
