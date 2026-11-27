using BandUp.Application.Abstractions;
using BandUp.Domain.Writing;

namespace BandUp.Application.Features.Writing.Finalization;

// These commands have no external transport. Card 3 must verify processing-attempt ownership
// under the submission lock before a production worker may complete or fail a submission.
internal sealed record ClaimWriting(Guid SubmissionId) : ICommand<bool>;
internal sealed record CompleteWriting(Guid SubmissionId, WritingFeedback Feedback) : ICommand<bool>;
internal sealed record FailWriting(Guid SubmissionId) : ICommand<bool>;
internal sealed record ExpireWriting(Guid SubmissionId) : ICommand<bool>;
