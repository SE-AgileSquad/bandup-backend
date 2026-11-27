using BandUp.Application.Features.Media.Admin;
using BandUp.Infrastructure.Configuration;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Options;

namespace BandUp.Infrastructure.Media;

public sealed class CloudinaryImageStorage(IOptions<CloudinarySettings> settings, IHttpClientFactory clients) : IImageStorage
{
    private readonly Lazy<Cloudinary> _client = new(() => CreateClient(settings.Value));

    private Cloudinary Client() => _client.Value;

    private static Cloudinary CreateClient(CloudinarySettings config)
    {
        if (string.IsNullOrWhiteSpace(config.CloudName) || string.IsNullOrWhiteSpace(config.ApiKey)
            || string.IsNullOrWhiteSpace(config.ApiSecret))
        {
            throw new ImageStorageException("Image storage is not configured.");
        }
        Cloudinary client = new(new Account(config.CloudName, config.ApiKey, config.ApiSecret));
        client.Api.Secure = true;
        client.Api.Timeout = 30_000;
        return client;
    }

    public async Task UploadAsync(string storageKey, Stream stream, string format, CancellationToken cancellationToken)
    {
        try
        {
            Cloudinary client = Client();
            var result = await client.UploadAsync(new ImageUploadParams
            {
                PublicId = storageKey, File = new FileDescription($"image.{format}", stream),
                Type = "authenticated", Overwrite = false, Format = format,
                Transformation = new Transformation().Width(1600).Height(1600).Crop("limit").Flags("strip_profile"),
            }, cancellationToken);
            if (result.Error is not null || result.PublicId != storageKey || result.Format != format
                || result.Width is <= 0 or > 1600 || result.Height is <= 0 or > 1600)
            {
                throw new ImageStorageException("The storage provider rejected the image.");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Provider error bodies may contain request URLs/credentials. Never expose them.
            throw new ImageStorageException("Image upload failed.");
        }
    }

    public async Task<byte[]> DownloadAsync(string storageKey, string format, CancellationToken cancellationToken)
    {
        try
        {
            string url = Client().Api.UrlImgUp.Secure(true).Signed(true).Type("authenticated").Format(format).BuildUrl(storageKey);
            using HttpResponseMessage response = await clients.CreateClient("CloudinaryImages")
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            const int maxBytes = 12 * 1024 * 1024;
            if (response.Content.Headers.ContentLength > maxBytes)
            {
                throw new ImageStorageException("Image exceeds the download limit.");
            }
            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using MemoryStream output = new();
            byte[] buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                if (output.Length + read > maxBytes)
                {
                    throw new ImageStorageException("Image exceeds the download limit.");
                }
                output.Write(buffer, 0, read);
            }
            return output.ToArray();
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            throw new ImageStorageException("Image download failed.");
        }
    }

    public async Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        try
        {
            var result = await Client().DestroyAsync(new DeletionParams(storageKey) { Type = "authenticated", Invalidate = true })
                .WaitAsync(cancellationToken);
            if (result.Error is not null || result.Result is not ("ok" or "not found"))
            {
                throw new ImageStorageException("Image deletion failed.");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            throw new ImageStorageException("Image deletion failed.");
        }
    }
}
