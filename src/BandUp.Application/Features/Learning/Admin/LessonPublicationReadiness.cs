using BandUp.Domain.Learning;

namespace BandUp.Application.Features.Learning.Admin;

public sealed record LessonPublicationReadiness(bool IsReady, IReadOnlyDictionary<string, string[]> Errors)
{
    public static LessonPublicationReadiness FromIssues(IEnumerable<LessonPublicationIssue> issues)
    {
        Dictionary<string, string[]> errors = issues.GroupBy(issue => issue.Field)
            .ToDictionary(group => group.Key, group => group.Select(issue => issue.Message).ToArray(), StringComparer.Ordinal);
        return new(errors.Count == 0, errors);
    }
}
