# How to add a feature

A step-by-step recipe. Every step names a real file from the reference feature
(`System`), so you can copy one that already works.

Read `docs/architecture/patterns.md` first if you want the *why*. This page is the *how*.

The worked example throughout is a hypothetical **lesson bookmark**: a user bookmarks a lesson,
and the platform indexes it in the background.

---

## 0. Before you start

- Pick a **module name** from the product's module list (Identity, Content, Exams, Learning,
  Credits, Writing, Assistant, Dashboard). The folder name, the schema name and the route
  prefix all use it.
- Pick or create a **scenario ID** for the behaviour. Every test you write is named after it.
- Decide whether it is a **command** (changes state) or a **query** (reads state). If it does
  both, it is a command.

---

## 1. Domain — only if there is an invariant

Create the entity in `src/BandUp.Domain/<Module>/`.

```
src/BandUp.Domain/Learning/Bookmark.cs
```

- Derive from `Entity`. Identity is a version-7 GUID assigned in the constructor.
- Put behaviour on the entity, not on a service. A method that can fail an invariant throws
  `DomainException` — that is a programming error, not an expected failure.
- If the entity needs the current time, **take it as a parameter**. The domain never reads the
  clock.

If the feature only reads existing data, skip this step entirely.

---

## 2. Contracts — only if the feature publishes a message

Create the message in `src/BandUp.Contracts/Messages/`.

```
src/BandUp.Contracts/Messages/BookmarkCreated.cs
```

- A `record`, with **ids only** — never user content, never free text.
- Then register the route in `MessageRoutes.CreateDefault()`:

```csharp
public static MessageRoutes CreateDefault() =>
    new MessageRoutes()
        .Map<PingRequested>(PingQueue)
        .Map<BookmarkCreated>("learning.bookmark-created");
```

That single line creates the queue in both processes, because both build the topology from
this table.

Skip this step if the feature has nothing to do asynchronously.

---

## 3. Application — the use case

Create a folder per use case under `src/BandUp.Application/Features/<Module>/`.

```
Features/Learning/CreateBookmark/
    CreateBookmarkCommand.cs
    CreateBookmarkHandler.cs
    CreateBookmarkValidator.cs
```

**The request** — a `record` implementing `ICommand<TResponse>` or `IQuery<TResponse>`:

```csharp
public sealed record CreateBookmarkCommand(Guid LessonId) : ICommand<Guid>;
```

**The handler** — primary constructor, `CancellationToken` on the method, returns `Result`:

```csharp
public sealed class CreateBookmarkHandler(
    IBookmarkRepository bookmarks,
    IMessagePublisher publisher,
    TimeProvider timeProvider)
    : ICommandHandler<CreateBookmarkCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(
        CreateBookmarkCommand command,
        CancellationToken cancellationToken)
    { ... }
}
```

Rules that matter here:

- **Expected failures are returned.** `Result<Guid>.Failure(Error.NotFound("..."))`, never a
  throw.
- **Publish normally.** The foundation defers it until after the commit; do not try to defer it
  yourself.
- **Never touch `DbContext`, `IConnectionMultiplexer` or `IChannel`.** Those are Infrastructure
  types and the architecture test will fail the build.

**The validator** — FluentValidation, discovered by scan, no registration:

```csharp
public sealed class CreateBookmarkValidator : AbstractValidator<CreateBookmarkCommand>
{
    public CreateBookmarkValidator() =>
        RuleFor(command => command.LessonId).NotEmpty();
}
```

A validator is optional. If there is none, the validation behavior passes straight through.

**Reads use query services.** A query handler depends on an `I<Feature>Queries` interface that
projects straight into a DTO. No entities in responses, no mapping layer:

```csharp
public interface IBookmarkQueries
{
    Task<PagedResult<BookmarkDto>> ListAsync(Guid userId, PageRequest page, CancellationToken ct);
}
```

Implement it in `src/BandUp.Infrastructure/Persistence/Queries/<Module>/` with a `Select`.

---

## 4. Infrastructure — persistence

Only if the feature stores data.

**Entity configuration** in `Persistence/Configurations/<Module>/`:

```csharp
public sealed class BookmarkConfiguration : IEntityTypeConfiguration<Bookmark>
{
    public void Configure(EntityTypeBuilder<Bookmark> builder)
    {
        builder.ToTable("bookmarks", schema: "learning");
        builder.HasKey(bookmark => bookmark.Id);
        builder.HasEnumCheckConstraint<Bookmark, BookmarkStatus>(nameof(Bookmark.Status));
        builder.UseXminConcurrency();
    }
}
```

It is applied automatically — `OnModelCreating` scans the assembly.

**Repository** — only if you need more than the generic `GetByIdAsync`/`AddAsync`/`Remove`:

```csharp
public interface IBookmarkRepository : IRepository<Bookmark>
{
    Task<bool> ExistsAsync(Guid userId, Guid lessonId, CancellationToken ct);
}
```

Interface in Application, implementation in Infrastructure, registered in
`Infrastructure/DependencyInjection.cs`.

**Migration:**

```bash
dotnet ef migrations add AddBookmarks --project src/BandUp.Infrastructure --startup-project src/BandUp.Infrastructure --output-dir Persistence/Migrations
```

Read the generated file before committing it. It is normal for it to need a fix: a schema that
does not exist yet, a rename it could not infer, a default it guessed.

---

## 5. Api — the endpoint

Create or extend a group in `src/BandUp.Api/Endpoints/<Module>/`.

```csharp
public sealed class LearningEndpoints : IEndpointGroup
{
    public string Prefix => "learning";
    public bool RequiresDiagnostics => false;

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/bookmarks", CreateBookmarkAsync)
            .WithName("Learning_CreateBookmark")
            .Produces<CreateBookmarkResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> CreateBookmarkAsync(
        CreateBookmarkCommand command,
        IDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        Result<Guid> result = await dispatcher.SendAsync<CreateBookmarkCommand, Guid>(
            command, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created($"/api/learning/bookmarks/{result.Value}", new CreateBookmarkResponse(result.Value))
            : result.ToProblem(httpContext);
    }
}
```

No registration: groups are discovered by assembly scan and mapped under `/api/<prefix>`.

The endpoint does four things and nothing else — bind, dispatch, map the result, declare the
responses. If you find yourself writing a rule here, it belongs in the handler.

**Declare every response.** The OpenAPI document is the contract the frontend generates its
client from; an undeclared 400 is invisible to it.

---

## 6. Worker — the consumer

Only if the feature reacts to a message.

```csharp
public sealed class BookmarkCreatedConsumer(
    RabbitMqConnectionProvider connections,
    RabbitMqTopology topology,
    ICacheService cache,
    ILogger<BookmarkCreatedConsumer> logger)
    : RabbitMqConsumerBase<BookmarkCreated>(connections, topology, cache, logger)
{
    protected override string QueueName => "learning.bookmark-created";

    protected override async Task HandleAsync(BookmarkCreated message, CancellationToken cancellationToken)
    { ... }
}
```

Register it in the host: `builder.Services.AddHostedService<BookmarkCreatedConsumer>();`

Do **not** implement acknowledgement, retries or deduplication. They are inherited. Your handler
should still be safe to run twice, because the guarantee is a narrowed window, not a promise.

---

## 7. Tests

One test per scenario, named after its identifier.

| Where | What it covers |
|---|---|
| `tests/BandUp.UnitTests/Features/<Module>/` | handler logic, validator rules, with substitutes |
| `tests/BandUp.IntegrationTests/` | the endpoint over HTTP, and the round trip through RabbitMQ |
| `tests/BandUp.ArchitectureTests/` | nothing — layering is already covered |

```csharp
[Fact]
public async Task LES_07_Creating_A_Bookmark_Returns_Created_And_Publishes_A_Message()
```

Integration tests join the existing `BandUpCollection`, so they reuse the containers already
running rather than starting their own.

---

## 8. Checklist

- [ ] Request is a `record`, handler returns `Result`
- [ ] Every async method takes a `CancellationToken`
- [ ] Time comes from `TimeProvider`, never `DateTime.UtcNow`
- [ ] No Infrastructure types in Application or Domain
- [ ] Messages carry ids only
- [ ] Endpoint declares success **and** error responses
- [ ] Validator exists if input can be wrong
- [ ] Migration reviewed by hand
- [ ] Test named after the scenario ID
- [ ] `dotnet build -c Release` is clean and `dotnet test` is green

---

## 9. Common mistakes

| Symptom | Cause |
|---|---|
| `No service for type ICommandHandler<...>` | handler not in `BandUp.Application`, or a nested private class (the scan only registers public, top-level types) |
| Build fails with a layering error | Application referenced EF, ASP.NET, Redis or RabbitMQ |
| Endpoint returns 404 | group's `RequiresDiagnostics` is true and the flag is off, or `Prefix` is missing from the route |
| `traceId` differs from `X-Request-Id` | a response was built with `TypedResults.Problem` instead of `result.ToProblem(...)` |
| Migration is empty | the entity has no `IEntityTypeConfiguration`, or it is not in the Infrastructure assembly |
| Consumer never fires | the message is not mapped in `MessageRoutes`, so no queue is declared or bound |
| Message published but never consumed | consumer not registered as a hosted service, or `QueueName` does not match the route |
