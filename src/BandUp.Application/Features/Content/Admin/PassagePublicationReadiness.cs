using BandUp.Domain.Content;

namespace BandUp.Application.Features.Content.Admin;

public sealed record PassagePublicationReadiness(bool IsReady, IReadOnlyDictionary<string, string[]> Errors)
{
    public static PassagePublicationReadiness FromIssues(IEnumerable<PublicationIssue> issues)
    {
        Dictionary<string, string[]> errors = issues
            .GroupBy(issue => issue.QuestionId is { } id ? $"questions.{id}.{issue.Field}" : issue.Field)
            .ToDictionary(group => group.Key, group => group.Select(issue => issue.Message).ToArray(), StringComparer.Ordinal);
        return new(errors.Count == 0, errors);
    }
}
