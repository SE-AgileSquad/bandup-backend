using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using FluentValidation;

namespace BandUp.Application.Features.Media.Admin;

public sealed record UploadContentPhotoCommand(PhotoOwner Owner, Guid Id, byte[] Bytes) : ICommand<PhotoDetails>;
public sealed record DeleteContentPhotoCommand(PhotoOwner Owner, Guid Id) : ICommand<bool>;
public sealed record GetContentPhotoQuery(PhotoOwner Owner, Guid Id) : IQuery<PhotoDownload>;

public sealed class UploadContentPhotoValidator : AbstractValidator<UploadContentPhotoCommand>
{
    public UploadContentPhotoValidator()
    {
        RuleFor(command => command.Bytes).Must(bytes => ImageFileInfo.Inspect(bytes) is not null)
            .WithMessage("Upload a JPEG, PNG or WebP image up to 5 MB, at most 10000 pixels per side and 24 megapixels.")
            .OverridePropertyName("file");
    }
}

public sealed class UploadContentPhotoHandler(IAdminPhotoStore store, IImageStorage storage, ICurrentUser actor, TimeProvider clock)
    : ICommandHandler<UploadContentPhotoCommand, PhotoDetails>
{
    public async Task<Result<PhotoDetails>> HandleAsync(UploadContentPhotoCommand command, CancellationToken cancellationToken)
    {
        if (!actor.IsAuthenticated || actor.UserId is not { } actorId)
        {
            return Result<PhotoDetails>.Failure(Error.Unauthorized("An authenticated admin is required."));
        }
        PhotoOwnerState? owner = await store.LoadForUpdateAsync(command.Owner, command.Id, cancellationToken);
        if (owner is null)
        {
            return Result<PhotoDetails>.Failure(Error.NotFound("Content not found."));
        }
        if (owner.IsReadOnly)
        {
            return Result<PhotoDetails>.Failure(Error.Conflict("This content is read-only and cannot change its photo."));
        }
        ImageFileInfo info = ImageFileInfo.Inspect(command.Bytes)!;
        var image = await store.ReserveAsync(info, command.Bytes.Length, actorId, clock.GetUtcNow(), cancellationToken);
        try
        {
            using MemoryStream stream = new(command.Bytes, writable: false);
            await storage.UploadAsync(image.StorageKey, stream, image.Format, cancellationToken);
        }
        catch (ImageStorageException)
        {
            return Result<PhotoDetails>.Failure(new Error("IMAGE_STORAGE_UNAVAILABLE", "The image could not be uploaded. Try again.", ErrorType.Unavailable));
        }
        store.SetPhoto(command.Owner, command.Id, image.Id, clock.GetUtcNow());
        return Result<PhotoDetails>.Success(new(image.Id, PhotoUrls.For(command.Owner, command.Id), image.ContentType, image.SizeBytes));
    }
}

public sealed class DeleteContentPhotoHandler(IAdminPhotoStore store, TimeProvider clock) : ICommandHandler<DeleteContentPhotoCommand, bool>
{
    public async Task<Result<bool>> HandleAsync(DeleteContentPhotoCommand command, CancellationToken cancellationToken)
    {
        PhotoOwnerState? owner = await store.LoadForUpdateAsync(command.Owner, command.Id, cancellationToken);
        if (owner is null)
        {
            return Result<bool>.Failure(Error.NotFound("Content not found."));
        }
        if (owner.IsReadOnly)
        {
            return Result<bool>.Failure(Error.Conflict("This content is read-only and cannot change its photo."));
        }
        store.SetPhoto(command.Owner, command.Id, null, clock.GetUtcNow());
        return Result<bool>.Success(true);
    }
}

public sealed class GetContentPhotoHandler(IAdminPhotoStore store, IImageStorage storage) : IQueryHandler<GetContentPhotoQuery, PhotoDownload>
{
    public async Task<Result<PhotoDownload>> HandleAsync(GetContentPhotoQuery query, CancellationToken cancellationToken)
    {
        var image = await store.GetAsync(query.Owner, query.Id, cancellationToken);
        if (image is null)
        {
            return Result<PhotoDownload>.Failure(Error.NotFound("Photo not found."));
        }
        try
        {
            return Result<PhotoDownload>.Success(new(await storage.DownloadAsync(image.StorageKey, image.Format, cancellationToken), image.ContentType));
        }
        catch (ImageStorageException)
        {
            return Result<PhotoDownload>.Failure(new Error("IMAGE_STORAGE_UNAVAILABLE", "The image could not be loaded. Try again.", ErrorType.Unavailable));
        }
    }
}
