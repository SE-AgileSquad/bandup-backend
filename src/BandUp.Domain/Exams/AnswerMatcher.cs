using System.Text.RegularExpressions;

namespace BandUp.Domain.Exams;

/// <summary>
/// Decides whether a learner's answer matches an answer key. Comparison ignores case and
/// surrounding or repeated whitespace; it does not forgive spelling.
/// </summary>
public static partial class AnswerMatcher
{
    public static bool IsCorrect(string selectedAnswer, ScoringKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        string selected = Normalize(selectedAnswer);

        if (selected.Length == 0)
        {
            return false;
        }

        if (selected == Normalize(key.CorrectAnswer))
        {
            return true;
        }

        return key.AcceptedVariants.Any(variant => selected == Normalize(variant));
    }

    public static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : Whitespace().Replace(value.Trim(), " ").ToUpperInvariant();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
