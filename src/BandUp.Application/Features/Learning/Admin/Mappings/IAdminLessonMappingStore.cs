using BandUp.Domain.Learning;

namespace BandUp.Application.Features.Learning.Admin.Mappings;

public interface IAdminLessonMappingStore
{
    Task<IReadOnlyList<LessonMappingRow>> GetAsync(CancellationToken cancellationToken);
    Task LockChangesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<Lesson>> LoadLessonsForUpdateAsync(Guid[] ids, CancellationToken cancellationToken);
    Task SetDefaultsAsync(IReadOnlyList<LessonMappingSelection> selections,
        IReadOnlyList<Lesson> lessons, CancellationToken cancellationToken);
}
