using BandUp.Application.Abstractions;
using BandUp.Application.Common;

namespace BandUp.Application.Features.Vocabulary.ListDueReviews;

public sealed record ListDueReviewsQuery(PageRequest Page) : IQuery<PagedResult<DueVocabularyReviewDto>>;
