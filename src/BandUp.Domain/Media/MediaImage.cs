using BandUp.Domain.Common;

namespace BandUp.Domain.Media;

/// <summary>A managed storage object, reserved before upload so interrupted requests can be cleaned up.</summary>
public sealed class MediaImage : Entity
{
    private MediaImage() { }
    public string StorageKey { get; private set; } = null!;
    public string Format { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public long SizeBytes { get; private set; }
    public Guid UploadedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static MediaImage Reserve(string format, string contentType, long sizeBytes, Guid uploadedBy, DateTimeOffset now)
    {
        if ((format, contentType) is not (("jpg", "image/jpeg") or ("png", "image/png") or ("webp", "image/webp")))
        {
            throw new DomainException("Unsupported image format.");
        }
        if (sizeBytes <= 0 || uploadedBy == Guid.Empty)
        {
            throw new DomainException("A photo requires a file and an uploading user.");
        }
        MediaImage image = new() { Format = format, ContentType = contentType, SizeBytes = sizeBytes,
            UploadedBy = uploadedBy, CreatedAt = now };
        image.StorageKey = $"bandup/{image.Id:N}";
        return image;
    }
}
