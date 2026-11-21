using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Content.Admin.GetAdminQuestion;

public sealed record GetAdminQuestionQuery(Guid PassageId, Guid QuestionId) : IQuery<QuestionDetails>;
