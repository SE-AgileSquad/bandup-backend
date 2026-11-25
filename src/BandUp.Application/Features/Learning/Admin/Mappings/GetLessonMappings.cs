using BandUp.Application.Abstractions;
using BandUp.Application.Common;

namespace BandUp.Application.Features.Learning.Admin.Mappings;

public sealed record GetLessonMappingsQuery : IQuery<IReadOnlyList<LessonMappingRow>>;

public sealed class GetLessonMappingsHandler(IAdminLessonMappingStore store)
    : IQueryHandler<GetLessonMappingsQuery, IReadOnlyList<LessonMappingRow>>
{
    public async Task<Result<IReadOnlyList<LessonMappingRow>>> HandleAsync(
        GetLessonMappingsQuery query, CancellationToken cancellationToken) =>
        Result<IReadOnlyList<LessonMappingRow>>.Success(await store.GetAsync(cancellationToken));
}
