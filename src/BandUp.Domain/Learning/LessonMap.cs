using BandUp.Domain.Common;
using BandUp.Domain.Content;

namespace BandUp.Domain.Learning;

public sealed class LessonMap : Entity
{
    private LessonMap()
    {
    }

    public QuestionType QuestionType { get; private set; }
    public string? ErrorPattern { get; private set; }
    public Guid LessonId { get; private set; }

    public static LessonMap Create(Lesson lesson, string? errorPattern = null)
    {
        ArgumentNullException.ThrowIfNull(lesson);

        if (errorPattern is not null && string.IsNullOrWhiteSpace(errorPattern))
        {
            throw new DomainException("An error pattern must have a value or be null for the default mapping.");
        }

        return new LessonMap
        {
            LessonId = lesson.Id,
            QuestionType = lesson.QuestionType,
            ErrorPattern = errorPattern?.Trim(),
        };
    }
}
