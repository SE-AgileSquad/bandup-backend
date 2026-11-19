namespace BandUp.Domain.Exams;

/// <summary>
/// Turns a raw Reading score into the approximate band shown with a result. The practice
/// passages are shorter than the real 40-question paper, so the score is first scaled to 40
/// questions and then looked up in the widely published IELTS Academic Reading conversion table.
/// The result is an estimate, never an official score, and the product always labels it so.
/// </summary>
public static class BandEstimator
{
    /// <summary>The number of questions in the paper the published table is written for.</summary>
    public const int FullPaperQuestions = 40;

    // Lowest scaled score (out of 40) that earns each band, highest band first. The rows from 9.0
    // down to 2.5 are the published table; the three rows below it (2.0, 1.0, 0.0) extend it for
    // the very lowest scores and are not part of that table.
    private static readonly (int MinimumScaledScore, decimal Band)[] Table =
    [
        (39, 9.0m),
        (37, 8.5m),
        (35, 8.0m),
        (33, 7.5m),
        (30, 7.0m),
        (27, 6.5m),
        (23, 6.0m),
        (19, 5.5m),
        (15, 5.0m),
        (13, 4.5m),
        (10, 4.0m),
        (8, 3.5m),
        (6, 3.0m),
        (4, 2.5m),
        (2, 2.0m),
        (1, 1.0m),
        (0, 0.0m),
    ];

    /// <summary>
    /// The approximate band for <paramref name="rawScore"/> correct answers out of
    /// <paramref name="totalQuestions"/>, or null when there are no questions to score.
    /// </summary>
    public static decimal? Estimate(int rawScore, int totalQuestions)
    {
        if (totalQuestions <= 0)
        {
            return null;
        }

        int correct = Math.Clamp(rawScore, 0, totalQuestions);
        int scaled = (int)Math.Round(
            correct * (decimal)FullPaperQuestions / totalQuestions,
            MidpointRounding.AwayFromZero);

        foreach ((int minimumScaledScore, decimal band) in Table)
        {
            if (scaled >= minimumScaledScore)
            {
                return band;
            }
        }

        return 0.0m;
    }
}
