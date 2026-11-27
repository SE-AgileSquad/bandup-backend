using BandUp.Domain.Media;

namespace BandUp.Application.Features.Media.Admin;

public enum PhotoOwner { Passage, WritingPrompt, Lesson }
public sealed record PhotoDetails(Guid Id, string Url, string ContentType, long SizeBytes);
public sealed record PhotoOwnerState(bool IsReadOnly, Guid? PhotoId);
public sealed record PhotoDownload(byte[] Bytes, string ContentType);
public sealed class ImageStorageException(string message) : Exception(message);

public interface IImageStorage
{
    Task UploadAsync(string storageKey, Stream stream, string format, CancellationToken cancellationToken);
    Task<byte[]> DownloadAsync(string storageKey, string format, CancellationToken cancellationToken);
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken);
}

public interface IAdminPhotoStore
{
    Task<PhotoOwnerState?> LoadForUpdateAsync(PhotoOwner owner, Guid id, CancellationToken cancellationToken);
    void SetPhoto(PhotoOwner owner, Guid id, Guid? photoId, DateTimeOffset now);
    Task<MediaImage> ReserveAsync(ImageFileInfo info, int size, Guid actorId, DateTimeOffset now, CancellationToken cancellationToken);
    Task<MediaImage?> GetAsync(PhotoOwner owner, Guid id, CancellationToken cancellationToken);
}

public static class PhotoUrls
{
    public static string For(PhotoOwner owner, Guid id) => owner switch
    {
        PhotoOwner.Passage => $"/api/admin/passages/{id}/photo",
        PhotoOwner.WritingPrompt => $"/api/admin/writing-prompts/{id}/photo",
        PhotoOwner.Lesson => $"/api/admin/lessons/{id}/photo",
        _ => throw new ArgumentOutOfRangeException(nameof(owner)),
    };
}
