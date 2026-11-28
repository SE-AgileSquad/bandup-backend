using BandUp.Application.Abstractions;
using BandUp.Application.Common;

namespace BandUp.Application.Features.Writing.Publishing;

public readonly record struct WritingPublishReservation(Guid SubmissionId, Guid LeaseId);

/// <summary>Returns one committed reservation. Publish externally only after this command returns.</summary>
public sealed record ReserveWritingPublication : ICommand<WritingPublishReservation?>;
public sealed record ConfirmWritingPublication(WritingPublishReservation Reservation) : ICommand<bool>;
public sealed record RecordWritingPublishFailure(WritingPublishReservation Reservation) : ICommand<bool>;

/// <summary>Lease operations use the caller's short Read Committed transaction and never perform broker I/O.</summary>
internal interface IWritingPublishingStore
{
    Task<WritingPublishReservation?> ReserveAsync(CancellationToken ct);
    Task<bool> RecordOutcomeAsync(WritingPublishReservation reservation, bool confirmed, CancellationToken ct);
}

internal sealed class WritingPublishingHandler(IWritingPublishingStore store) :
    ICommandHandler<ReserveWritingPublication, WritingPublishReservation?>,
    ICommandHandler<ConfirmWritingPublication, bool>,
    ICommandHandler<RecordWritingPublishFailure, bool>
{
    public async Task<Result<WritingPublishReservation?>> HandleAsync(ReserveWritingPublication command, CancellationToken ct) =>
        Result<WritingPublishReservation?>.Success(await store.ReserveAsync(ct));

    public async Task<Result<bool>> HandleAsync(ConfirmWritingPublication command, CancellationToken ct) =>
        Result<bool>.Success(await store.RecordOutcomeAsync(command.Reservation, confirmed: true, ct));

    public async Task<Result<bool>> HandleAsync(RecordWritingPublishFailure command, CancellationToken ct) =>
        Result<bool>.Success(await store.RecordOutcomeAsync(command.Reservation, confirmed: false, ct));
}
