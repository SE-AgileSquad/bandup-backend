using BandUp.Application.Abstractions;
using BandUp.Application.Features.Media.Admin;
using BandUp.Api.Errors;

namespace BandUp.Api.Endpoints.Admin;

internal sealed class PhotoUploadMetadata;

internal static class AdminPhotoEndpoints
{
    internal static void MapPhotoEndpoints(this RouteGroupBuilder content, PhotoOwner owner)
    {
        string name = owner switch
        {
            PhotoOwner.Passage => "Passage",
            PhotoOwner.WritingPrompt => "WritingPrompt",
            PhotoOwner.Lesson => "Lesson",
            _ => throw new ArgumentOutOfRangeException(nameof(owner)),
        };
        // Bind the owner's route parameter explicitly so ApiExplorer includes it in Swagger.
        // Reading only HttpContext.RouteValues leaves the required path parameter undocumented.
        Delegate upload, delete, get;
        if (owner == PhotoOwner.Passage)
        {
            upload = (Guid passageId, IFormFile file, IDispatcher dispatcher, HttpContext http) =>
                UploadAsync(owner, passageId, file, dispatcher, http);
            delete = (Guid passageId, IDispatcher dispatcher, HttpContext http) =>
                DeleteAsync(owner, passageId, dispatcher, http);
            get = (Guid passageId, IDispatcher dispatcher, HttpContext http) =>
                GetAsync(owner, passageId, dispatcher, http);
        }
        else if (owner == PhotoOwner.WritingPrompt)
        {
            upload = (Guid promptId, IFormFile file, IDispatcher dispatcher, HttpContext http) =>
                UploadAsync(owner, promptId, file, dispatcher, http);
            delete = (Guid promptId, IDispatcher dispatcher, HttpContext http) =>
                DeleteAsync(owner, promptId, dispatcher, http);
            get = (Guid promptId, IDispatcher dispatcher, HttpContext http) =>
                GetAsync(owner, promptId, dispatcher, http);
        }
        else
        {
            upload = (Guid lessonId, IFormFile file, IDispatcher dispatcher, HttpContext http) =>
                UploadAsync(owner, lessonId, file, dispatcher, http);
            delete = (Guid lessonId, IDispatcher dispatcher, HttpContext http) =>
                DeleteAsync(owner, lessonId, dispatcher, http);
            get = (Guid lessonId, IDispatcher dispatcher, HttpContext http) =>
                GetAsync(owner, lessonId, dispatcher, http);
        }

        content.MapPost("/photo", upload)
            // The application's scoped multipart + custom-header CSRF check runs before binding.
            .DisableAntiforgery().WithMetadata(new PhotoUploadMetadata())
            .WithName($"Admin_Upload{name}Photo").Produces<PhotoDetails>()
            .ProducesProblem(400).ProducesProblem(404).ProducesProblem(409).ProducesProblem(413).ProducesProblem(502);

        content.MapDelete("/photo", delete)
            .WithName($"Admin_Delete{name}Photo").Produces(204).ProducesProblem(404).ProducesProblem(409);

        content.MapGet("/photo", get)
            .WithName($"Admin_Get{name}Photo").Produces(200).ProducesProblem(404).ProducesProblem(502);
    }

    private static async Task<IResult> UploadAsync(PhotoOwner owner, Guid id, IFormFile file, IDispatcher dispatcher, HttpContext http)
    {
        if (file.Length is <= 0 or > ImageFileInfo.MaxFileBytes)
        {
            return BandUp.Application.Common.Error.Validation(new Dictionary<string, string[]>
                { ["file"] = ["Upload an image between 1 byte and 5 MB."] }).ToProblem(http);
        }
        using MemoryStream buffer = new();
        await file.CopyToAsync(buffer, http.RequestAborted);
        var result = await dispatcher.SendAsync<UploadContentPhotoCommand, PhotoDetails>(
            new(owner, id, buffer.ToArray()), http.RequestAborted);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(http);
    }

    private static Task<IResult> DeleteAsync(PhotoOwner owner, Guid id, IDispatcher dispatcher, HttpContext http) =>
        AdminContentHttp.ChangeAsync(new DeleteContentPhotoCommand(owner, id), dispatcher, http);

    private static async Task<IResult> GetAsync(PhotoOwner owner, Guid id, IDispatcher dispatcher, HttpContext http)
    {
        var result = await dispatcher.QueryAsync<GetContentPhotoQuery, PhotoDownload>(new(owner, id), http.RequestAborted);
        if (result.IsFailure)
        {
            return result.ToProblem(http);
        }
        http.Response.Headers.CacheControl = "private, no-store";
        http.Response.Headers.XContentTypeOptions = "nosniff";
        return TypedResults.File(result.Value.Bytes, result.Value.ContentType);
    }
}
