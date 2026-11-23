using BandUp.Application.Common;
using BandUp.Domain.Learning;

namespace BandUp.Application.Features.Learning.Admin;

public interface IAdminLessonStore
{
    Task<Lesson?> LoadForUpdateAsync(Guid id, CancellationToken cancellationToken);
    Task AddAsync(Lesson lesson, CancellationToken cancellationToken);
    Task<bool> TryDeleteLessonAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> TryDeleteExampleAsync(Guid lessonId, Guid exampleId, CancellationToken cancellationToken);
    Task<PagedResult<LessonSummary>> ListAsync(
        PageRequest page, LessonStatus? status, string? search, CancellationToken cancellationToken);
    Task<LessonDetails?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<LessonExampleDetails?> GetExampleAsync(Guid lessonId, Guid exampleId, CancellationToken cancellationToken);
}
