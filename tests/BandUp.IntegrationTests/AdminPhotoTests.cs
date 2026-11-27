using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BandUp.Application.Abstractions;
using BandUp.Application.Features.Media.Admin;
using BandUp.Domain.Identity;
using BandUp.Infrastructure;
using BandUp.Infrastructure.Media;
using BandUp.Infrastructure.Persistence;
using BandUp.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BandUp.IntegrationTests;

[Collection(BandUpCollection.Name)]
public sealed class AdminPhotoTests(BandUpTestEnvironment environment)
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    [Fact]
    public async Task Swagger_Exposes_Required_Owner_Ids_On_All_Photo_Operations()
    {
        await using BandUpApiFactory factory = Factory(new FakeImages());
        using HttpClient client = factory.CreateClient();
        JsonElement spec = await client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
        foreach ((string collection, string parameter) in new[] { ("passages", "passageId"), ("writing-prompts", "promptId"), ("lessons", "lessonId") })
        {
            JsonElement path = spec.GetProperty("paths").GetProperty($"/api/admin/{collection}/{{{parameter}}}/photo");
            foreach (string method in new[] { "get", "post", "delete" })
            {
                JsonElement id = path.GetProperty(method).GetProperty("parameters").EnumerateArray()
                    .Single(item => item.GetProperty("name").GetString() == parameter);
                id.GetProperty("in").GetString().ShouldBe("path");
                id.GetProperty("required").GetBoolean().ShouldBeTrue();
                id.GetProperty("schema").GetProperty("format").GetString().ShouldBe("uuid");
            }
        }
    }

    [Theory]
    [InlineData(false, Role.Admin)]
    [InlineData(true, Role.SuperAdmin)]
    public async Task Photo_Lifecycle_Updates_List_And_Details_And_Does_Not_Delete_The_Previous_Object_Early(bool writing, Role role)
    {
        FakeImages images = new();
        await using BandUpApiFactory factory = Factory(images);
        using HttpClient client = await ClientAsync(factory, role);
        string path = await CreateAsync(client, writing);
        (await DetailsAsync(client, path)).GetProperty("photo").ValueKind.ShouldBe(JsonValueKind.Null);
        using (HttpResponseMessage missing = await client.GetAsync($"{path}/photo"))
        {
            missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
        PhotoDetails first = await UploadAsync(client, path);
        first.Url.ShouldBe($"{path}/photo");
        first.ContentType.ShouldBe("image/png");
        first.SizeBytes.ShouldBe(Png.Length);
        JsonElement details = await DetailsAsync(client, path);
        details.GetProperty("photo").GetProperty("id").GetGuid().ShouldBe(first.Id);
        JsonElement page = await client.GetFromJsonAsync<JsonElement>($"{path[..path.LastIndexOf('/')] }?search={details.GetProperty("title").GetString()}");
        page.GetProperty("items")[0].GetProperty("photo").GetProperty("id").GetGuid().ShouldBe(first.Id);
        using (HttpResponseMessage download = await client.GetAsync(first.Url))
        {
            download.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await download.Content.ReadAsByteArrayAsync()).ShouldBe(Png);
            download.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
            download.Headers.CacheControl!.NoStore.ShouldBeTrue();
        }
        PhotoDetails second = await UploadAsync(client, path);
        second.Id.ShouldNotBe(first.Id);
        images.Files.Count.ShouldBe(2);
        await DeleteAsync(client, path);
        (await DetailsAsync(client, path)).GetProperty("photo").ValueKind.ShouldBe(JsonValueKind.Null);
        DateTimeOffset timestamp = (await DetailsAsync(client, path)).GetProperty("updatedAt").GetDateTimeOffset();
        await DeleteAsync(client, path);
        (await DetailsAsync(client, path)).GetProperty("updatedAt").GetDateTimeOffset().ShouldBe(timestamp);
        images.Deleted.ShouldBeEmpty();
        (await CleanupAsync(factory)).ShouldBe(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Published_And_Archived_Photos_Are_Immutable_And_Copies_Keep_Their_Photo(bool writing)
    {
        FakeImages images = new();
        await using BandUpApiFactory factory = Factory(images);
        using HttpClient client = await ClientAsync(factory, Role.Admin);
        string path = await CreateAsync(client, writing);
        PhotoDetails photo = await UploadAsync(client, path);
        if (!writing)
        {
            using HttpResponseMessage question = await client.PostAsJsonAsync($"{path}/questions", new
            {
                type = "TrueFalseNotGiven", prompt = "Some gardens received funding.", sortOrder = 1,
                correctAnswer = "True", explanation = "The passage confirms this.",
                evidenceSentence = "Some gardens received funding.", isVerified = true,
            });
            question.StatusCode.ShouldBe(HttpStatusCode.Created, await question.Content.ReadAsStringAsync());
        }
        using (HttpResponseMessage published = await client.PostAsJsonAsync($"{path}/publish", new { reviewConfirmed = true }))
        {
            published.StatusCode.ShouldBe(HttpStatusCode.NoContent, await published.Content.ReadAsStringAsync());
        }
        await UploadAsync(client, path, HttpStatusCode.Conflict);
        await DeleteAsync(client, path, HttpStatusCode.Conflict);
        string suffix = writing ? "duplicate" : "revisions";
        using HttpResponseMessage copyResponse = await client.PostAsJsonAsync($"{path}/{suffix}", new { });
        copyResponse.StatusCode.ShouldBe(HttpStatusCode.Created, await copyResponse.Content.ReadAsStringAsync());
        string copy = copyResponse.Headers.Location!.OriginalString;
        (await DetailsAsync(client, copy)).GetProperty("photo").GetProperty("id").GetGuid().ShouldBe(photo.Id);
        await DeleteAsync(client, copy);
        (await DetailsAsync(client, path)).GetProperty("photo").GetProperty("id").GetGuid().ShouldBe(photo.Id);
        (await CleanupAsync(factory)).ShouldBe(0);
        using (HttpResponseMessage archived = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, path) { Content = JsonContent.Create(new { }) }))
        {
            archived.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }
        await UploadAsync(client, path, HttpStatusCode.Conflict);
        await DeleteAsync(client, path, HttpStatusCode.Conflict);
        using HttpResponseMessage preserved = await client.GetAsync($"{path}/photo");
        preserved.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_Storage_And_Database_Writes_Preserve_Old_Photo_And_Durable_Orphans_Are_Retried(bool lesson)
    {
        FakeImages images = new();
        FailSave control = new();
        await using BandUpApiFactory factory = Factory(images, control);
        using HttpClient client = await ClientAsync(factory, Role.SuperAdmin);
        string path = lesson ? await CreateLessonAsync(client) : await CreateAsync(client, true);
        PhotoDetails original = await UploadAsync(client, path);
        images.FailUpload = true;
        await UploadAsync(client, path, HttpStatusCode.BadGateway);
        images.FailUpload = false;
        control.Enabled = true;
        await UploadAsync(client, path, HttpStatusCode.InternalServerError);
        control.Enabled = false;
        (await DetailsAsync(client, path)).GetProperty("photo").GetProperty("id").GetGuid().ShouldBe(original.Id);
        images.FailDownload = true;
        using (HttpResponseMessage failed = await client.GetAsync($"{path}/photo"))
        {
            failed.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
            (await failed.Content.ReadAsStringAsync()).ShouldNotContain("secret");
        }
        images.FailDownload = false;
        (await CleanupAsync(factory, future: false)).ShouldBe(0);
        images.FailDelete = true;
        (await CleanupAsync(factory)).ShouldBe(0);
        images.FailDelete = false;
        (await CleanupAsync(factory)).ShouldBe(2);
        images.Files.Count.ShouldBe(1);
        await DeleteAsync(client, path);
        (await CleanupAsync(factory)).ShouldBe(1);
        images.Files.ShouldBeEmpty();
    }

    [Fact]
    public async Task Concurrent_Replacements_Always_Link_One_Complete_Image()
    {
        FakeImages images = new();
        await using BandUpApiFactory factory = Factory(images);
        using HttpClient client = await ClientAsync(factory, Role.Admin);
        string path = await CreateAsync(client, false);
        PhotoDetails[] photos = await Task.WhenAll(UploadAsync(client, path), UploadAsync(client, path));
        Guid selected = (await DetailsAsync(client, path)).GetProperty("photo").GetProperty("id").GetGuid();
        photos.Select(item => item.Id).ShouldContain(selected);
        (await CleanupAsync(factory)).ShouldBe(1);
        using HttpResponseMessage download = await client.GetAsync($"{path}/photo");
        download.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Upload_Requires_Admin_Header_Valid_File_And_Keeps_Multipart_Exception_Scoped_To_Photos(bool lesson)
    {
        FakeImages images = new();
        await using BandUpApiFactory factory = Factory(images);
        using HttpClient admin = await ClientAsync(factory, Role.Admin);
        string path = lesson ? await CreateLessonAsync(admin) : await CreateAsync(admin, true);
        using HttpClient anonymous = factory.CreateClient();
        await UploadAsync(anonymous, path, HttpStatusCode.Unauthorized);
        using HttpClient user = await ClientAsync(factory, Role.User);
        await UploadAsync(user, path, HttpStatusCode.Forbidden);
        using (HttpResponseMessage forbidden = await user.GetAsync($"{path}/photo"))
        {
            forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
        admin.DefaultRequestHeaders.Remove("X-Requested-With");
        await UploadAsync(admin, path, HttpStatusCode.BadRequest);
        admin.DefaultRequestHeaders.Add("X-Requested-With", "bandup");
        await UploadAsync(admin, path, HttpStatusCode.BadRequest, "not an image"u8.ToArray());
        await UploadAsync(admin, path, HttpStatusCode.BadRequest, []);
        await UploadAsync(admin, path, HttpStatusCode.RequestEntityTooLarge, new byte[ImageFileInfo.MaxRequestBytes + 1]);
        using (HttpResponseMessage missing = await admin.PostAsync($"{path}/photo", new MultipartFormDataContent()))
        {
            missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
        string collection = lesson ? "lessons" : "writing-prompts";
        string parameter = lesson ? "lessonId" : "promptId";
        await UploadAsync(admin, $"/api/admin/{collection}/{Guid.NewGuid()}", HttpStatusCode.NotFound);
        using (MultipartFormDataContent form = Form(Png))
        using (HttpResponseMessage wrongEndpoint = await admin.PostAsync($"/api/admin/{collection}", form))
        {
            wrongEndpoint.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
        images.Files.ShouldBeEmpty();
        JsonElement spec = await admin.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
        spec.GetProperty("paths").GetProperty($"/api/admin/{collection}/{{{parameter}}}/photo")
            .GetProperty("post").GetProperty("requestBody").GetProperty("content")
            .TryGetProperty("multipart/form-data", out _).ShouldBeTrue();
    }

    [Theory]
    [InlineData(Role.Admin)]
    [InlineData(Role.SuperAdmin)]
    public async Task Lesson_Photos_Follow_Lesson_Editability_And_Remain_Protected_From_Cleanup_Until_Removed(Role role)
    {
        FakeImages images = new();
        await using BandUpApiFactory factory = Factory(images);
        using HttpClient client = await ClientAsync(factory, role);
        string path = await CreateLessonAsync(client);
        (await DetailsAsync(client, path)).GetProperty("photo").ValueKind.ShouldBe(JsonValueKind.Null);
        using (HttpResponseMessage absent = await client.GetAsync($"{path}/photo"))
        {
            absent.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
        PhotoDetails first = await UploadAsync(client, path);
        first.Url.ShouldBe($"{path}/photo");
        JsonElement details = await DetailsAsync(client, path);
        details.GetProperty("photo").GetProperty("id").GetGuid().ShouldBe(first.Id);
        JsonElement page = await client.GetFromJsonAsync<JsonElement>($"/api/admin/lessons?search={details.GetProperty("title").GetString()}");
        page.GetProperty("items")[0].GetProperty("photo").GetProperty("id").GetGuid().ShouldBe(first.Id);
        using (HttpResponseMessage edit = await client.PutAsJsonAsync(path, new { title = "Updated lesson", body = "Updated text", sortOrder = 1 }))
        {
            edit.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }
        (await DetailsAsync(client, path)).GetProperty("photo").GetProperty("id").GetGuid().ShouldBe(first.Id);
        using (HttpResponseMessage example = await client.PostAsJsonAsync($"{path}/examples", new
        {
            evidenceSentence = "Birds build nests.", paraphrase = "Nest building", sortOrder = 1,
        }))
        {
            example.StatusCode.ShouldBe(HttpStatusCode.Created);
        }
        using (HttpResponseMessage publish = await client.PostAsJsonAsync($"{path}/publish", new { }))
        {
            publish.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }
        PhotoDetails second = await UploadAsync(client, path);
        second.Id.ShouldNotBe(first.Id);
        (await CleanupAsync(factory)).ShouldBe(1);
        images.Files.Count.ShouldBe(1);
        using (HttpResponseMessage download = await client.GetAsync(second.Url))
        {
            download.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await download.Content.ReadAsByteArrayAsync()).ShouldBe(Png);
        }
        await DeleteAsync(client, path);
        await DeleteAsync(client, path);
        (await DetailsAsync(client, path)).GetProperty("photo").ValueKind.ShouldBe(JsonValueKind.Null);
        (await CleanupAsync(factory)).ShouldBe(1);
        PhotoDetails archivedPhoto = await UploadAsync(client, path);
        using (HttpResponseMessage archive = await client.PostAsJsonAsync($"{path}/archive", new { }))
        {
            archive.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }
        await UploadAsync(client, path, HttpStatusCode.Conflict);
        await DeleteAsync(client, path, HttpStatusCode.Conflict);
        (await CleanupAsync(factory)).ShouldBe(0);
        using (HttpResponseMessage download = await client.GetAsync(archivedPhoto.Url))
        {
            download.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        using (HttpResponseMessage delete = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, path) { Content = JsonContent.Create(new { }) }))
        {
            delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }
        (await CleanupAsync(factory)).ShouldBe(1);
        images.Files.ShouldBeEmpty();
    }

    private static async Task<string> CreateLessonAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/admin/lessons", new
        {
            title = Guid.NewGuid().ToString("N"), body = "Lesson text", questionType = "TrueFalseNotGiven", sortOrder = 1,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return response.Headers.Location!.OriginalString;
    }

    private BandUpApiFactory Factory(FakeImages images, FailSave? control = null) => new(environment, configureServices: services =>
    {
        services.RemoveAll<IImageStorage>();
        services.AddSingleton<IImageStorage>(images);
        if (control is not null)
        {
            services.RemoveAll<IUnitOfWork>();
            services.AddScoped<IUnitOfWork>(provider => new FailingUnitOfWork(new EfUnitOfWork(provider.GetRequiredService<BandUpDbContext>()), control));
        }
    });

    private async Task<HttpClient> ClientAsync(BandUpApiFactory factory, Role role)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        User actor = User.Create($"{Guid.NewGuid():N}@example.test", "Photo editor", "test-hash", now);
        actor.ChangeRole(role);
        await using BandUpDbContext db = BandUpTestEnvironment.CreateContext(environment.MigratedConnectionString);
        db.Users.Add(actor);
        await db.SaveChangesAsync();
        HttpClient client = factory.CreateClient();
        string token = factory.Services.GetRequiredService<ITokenService>().CreateAccessToken(actor.Id, role.ToString(), now).Value;
        client.DefaultRequestHeaders.Add("Cookie", $"bu_at={token}");
        client.DefaultRequestHeaders.Add("X-Requested-With", "bandup");
        return client;
    }

    private static async Task<string> CreateAsync(HttpClient client, bool writing)
    {
        string title = Guid.NewGuid().ToString("N");
        using HttpResponseMessage response = writing
            ? await client.PostAsJsonAsync("/api/admin/writing-prompts", new { title, taskText = "Discuss transport." })
            : await client.PostAsJsonAsync("/api/admin/passages", new { title, body = "Some gardens received funding.", sourceNote = "Original", usageBasis = "Original" });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return response.Headers.Location!.OriginalString;
    }

    private static async Task<JsonElement> DetailsAsync(HttpClient client, string path) => await client.GetFromJsonAsync<JsonElement>(path);

    private static MultipartFormDataContent Form(byte[] bytes)
    {
        MultipartFormDataContent form = new();
        ByteArrayContent file = new(bytes);
        // MIME and filename are deliberately untrusted. Validation must use the actual bytes.
        file.Headers.ContentType = new("application/octet-stream");
        form.Add(file, "file", "untrusted.dat");
        return form;
    }

    private static async Task<PhotoDetails> UploadAsync(HttpClient client, string path, HttpStatusCode expected = HttpStatusCode.OK, byte[]? bytes = null)
    {
        using MultipartFormDataContent form = Form(bytes ?? Png);
        using HttpResponseMessage response = await client.PostAsync($"{path}/photo", form);
        response.StatusCode.ShouldBe(expected, await response.Content.ReadAsStringAsync());
        return expected == HttpStatusCode.OK ? (await response.Content.ReadFromJsonAsync<PhotoDetails>())! : null!;
    }

    private static async Task DeleteAsync(HttpClient client, string path, HttpStatusCode expected = HttpStatusCode.NoContent)
    {
        using HttpRequestMessage request = new(HttpMethod.Delete, $"{path}/photo") { Content = JsonContent.Create(new { }) };
        using HttpResponseMessage response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(expected, await response.Content.ReadAsStringAsync());
    }

    private static Task<int> CleanupAsync(BandUpApiFactory factory, bool future = true) =>
        new ImageCleanupService(factory.Services.GetRequiredService<IServiceScopeFactory>(),
            new CleanupClock(DateTimeOffset.UtcNow.AddHours(future ? 2 : 0)), NullLogger<ImageCleanupService>.Instance).CleanAsync(CancellationToken.None);

    private sealed class CleanupClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeImages : IImageStorage
    {
        public ConcurrentDictionary<string, byte[]> Files { get; } = new();
        public ConcurrentBag<string> Deleted { get; } = [];
        public bool FailUpload { get; set; }
        public bool FailDownload { get; set; }
        public bool FailDelete { get; set; }
        public async Task UploadAsync(string storageKey, Stream stream, string format, CancellationToken cancellationToken)
        {
            if (FailUpload)
            {
                throw new ImageStorageException("secret provider error");
            }
            using MemoryStream buffer = new();
            await stream.CopyToAsync(buffer, cancellationToken);
            Files[storageKey] = buffer.ToArray();
        }
        public Task<byte[]> DownloadAsync(string storageKey, string format, CancellationToken cancellationToken) =>
            FailDownload ? throw new ImageStorageException("secret provider error") : Task.FromResult(Files[storageKey]);
        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
        {
            if (FailDelete)
            {
                throw new ImageStorageException("secret provider error");
            }
            Files.TryRemove(storageKey, out _);
            Deleted.Add(storageKey);
            return Task.CompletedTask;
        }
    }
    private sealed class FailSave { public bool Enabled { get; set; } }
    private sealed class FailingUnitOfWork(EfUnitOfWork inner, FailSave control) : IUnitOfWork
    {
        public Task BeginTransactionAsync(CancellationToken cancellationToken) => inner.BeginTransactionAsync(cancellationToken);
        public Task CommitTransactionAsync(CancellationToken cancellationToken) => inner.CommitTransactionAsync(cancellationToken);
        public Task RollbackTransactionAsync(CancellationToken cancellationToken) => inner.RollbackTransactionAsync(cancellationToken);
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => control.Enabled
            ? throw new InvalidOperationException("Simulated save failure") : inner.SaveChangesAsync(cancellationToken);
    }
}
