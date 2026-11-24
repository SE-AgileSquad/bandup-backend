using BandUp.Domain.Common;

namespace BandUp.Domain.Learning;

public sealed class LessonProgress : Entity
{
    private LessonProgress()
    {
    }

    public Guid UserId { get; private set; }
    public Guid LessonId { get; private set; }
    public DateTimeOffset CompletedAt { get; private set; }

    public static LessonProgress Complete(Guid userId, Guid lessonId, DateTimeOffset completedAt)
    {
        if (userId == Guid.Empty || lessonId == Guid.Empty)
        {
            throw new DomainException("Lesson progress requires a user and a lesson.");
        }

        return new LessonProgress
        {
            UserId = userId,
            LessonId = lessonId,
            CompletedAt = completedAt,
        };
    }
}
