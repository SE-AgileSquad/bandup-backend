# Patterns

Every pattern used in this codebase, where it lives, how to use it, and — more usefully —
when **not** to use it.

Each entry points at the `System` reference slice, which is the template to copy. The slice is
`Application/Features/System/`, `Api/Endpoints/System/` and `Worker/Consumers/`.

For the whole system on one page — the six projects, the runtime view, a request end to end —
read [backend-architecture.md](backend-architecture.md). For the step-by-step recipe, read
[how-to-add-a-feature.md](../how-to-add-a-feature.md).

---

## CQRS-lite with an in-house dispatcher

**What.** Commands and queries are `record`s. A hand-written `IDispatcher` resolves the single
handler for a request and runs it through the behavior chain. See
[ADR 0002](../adr/0002-cqrs-lite-with-in-house-dispatcher.md).

**Where.** `Application/Abstractions/`, `Application/Dispatcher.cs`, `Application/Features/<Module>/`.

**How.**

```csharp
public sealed record SendPing(string Message) : ICommand<Guid>;

public sealed class SendPingHandler(IMessagePublisher publisher, TimeProvider time)
    : ICommandHandler<SendPing, Guid>
{
    public async Task<Result<Guid>> HandleAsync(SendPing command, CancellationToken cancellationToken)
    { ... }
}
```

Call it with both type arguments — `SendAsync<SendPing, Guid>(command, ct)`. `TResponse` cannot
be inferred from the constraint.

**Do not use it when** the work is a pure function with no cross-cutting concerns. A domain
service called directly from a handler is simpler and faster; the dispatcher exists to apply
the pipeline, not to be a tax on every method call.

---

## Pipeline behaviors

**What.** Chain-of-responsibility around every request. Registration order is execution order,
outermost first.

**Where.** `Application/Behaviors/`.

**How.** Register in `AddPipelineBehaviors`, in this order: **Logging → Validation → Transaction**.
A behavior implements `IPipelineBehavior<TRequest, TResponse>` and calls `next()`.

Mark a behavior `ICommandOnlyPipelineBehavior` to exclude it from queries — that is how the
transaction behavior stays out of read paths.

**Current order is load-bearing.** Logging must be outermost so it sees the final outcome.
Validation must precede the transaction so an invalid command never opens one.

**Do not use it when** the concern is not uniform across requests. A cross-cutting rule that
applies to two of forty handlers belongs in those two handlers, not in the pipeline.

---

## Result pattern

**What.** Expected failures are returned, not thrown. See [ADR 0003](../adr/0003-result-pattern.md).

**Where.** `Application/Common/Result.cs`, `Error.cs`, `ErrorType.cs`. Mapping to HTTP lives in
`Api/Errors/ErrorHttpMapper.cs`.

**How.** Return `Result<T>.Success(value)` or `Result<T>.Failure(Error.NotFound("..."))`. In an
endpoint, `result.ToProblem(httpContext)` produces the RFC 7807 response.

**Do not use it when** the failure is a bug or an infrastructure fault. Those throw, and the
exception handler turns them into a `500`. `DomainException` marks a violated invariant.

---

## Repository and Unit of Work

**What.** Write-side persistence behind interfaces. `IRepository<T>` for the common operations,
`IUnitOfWork` for `SaveChangesAsync` and transaction control.

**Where.** Interfaces in `Application/Abstractions/`, implementations in
`Infrastructure/Persistence/`.

**How.** Register the generic repository with `IRepository<>`; add an aggregate-specific
interface when a feature needs more than `GetById`/`Add`/`Remove`.

**Do not use it when** you are reading. Reads go through query services — see below. A handler
that loads entities to return them to a caller is doing twice the work.

---

## Query services

**What.** The read side. A query handler depends on an `I<Feature>Queries` interface that
returns DTOs through EF projections.

**Where.** Interface in `Application/Abstractions/` or beside the feature, implementation in
`Infrastructure/Persistence/Queries/`.

**How.** Project straight into the DTO with `Select(...)`. No entities in responses, no mapping
layer, no `Include` chains. Paginated results use `PagedResult<T>` and `PageRequest`.

**Do not use it when** the read is a single row by id and the caller genuinely needs the entity
to make a decision. That is what `IRepository<T>.GetByIdAsync` is for.

---

## Adapter and Fake

**What.** Every external dependency sits behind an interface with a real and a fake
implementation, selected by `<Service>__Provider` configuration.

**Where.** Interfaces in `Application/Abstractions/`, implementations in `Infrastructure/`.
Currently `IMessagePublisher` (RabbitMQ / in-memory) and `ICacheService` (Redis / in-memory).
Time uses the built-in `TimeProvider`.

**How.** Configure with environment variables:

```
Cache__Provider=InMemory
MessagePublisher__Provider=InMemory
```

**Do not use it when** the dependency is part of the runtime itself. We do not wrap
`TimeProvider` or `IConfiguration`; they come from the framework and are already substitutable.

---

## Decorator

**What.** Wrap an implementation to add behaviour without touching it.

**Where.** Registered by hand in `Infrastructure/DependencyInjection.cs`. No Scrutor.

**How.** `BufferedMessagePublisher` decorates `IDirectMessagePublisher` to enforce
publish-after-commit; a cached repository would wrap the EF one the same way. Register the inner
implementation and the decorator separately, then resolve the interface in `Pipeline` order.

**Do not use it when** the order of registration is the only thing making it correct and a
reader would not notice. If the wrapping is subtle, say so in a comment at the registration.

---

## Strategy

**What.** Interchangeable algorithms behind one interface, chosen per case.

**Where.** Domain. Documented now, first used by the answer evaluators per question type.

**How.** A domain service takes the strategy. Because the domain cannot read the clock or use
DI, selection is passed in as an argument rather than resolved from a container.

**Do not use it when** there is one implementation and no second one in sight. A `switch`
statement is clearer than an interface with a single implementer.

---

## Options pattern

**What.** Strongly typed configuration with validation, registered to fail fast.

**Where.** `Infrastructure/Configuration/`. Bound in `Api/ApiConfiguration.cs` and
`Infrastructure/DependencyInjection.cs`.

**How.** A settings class per configuration section, bound with `BindConfiguration`, validated,
and finished with `ValidateOnStart()`. **A missing required value stops the process at startup
with a readable message** — it is never a null reference later.

Classes are named `...Settings` rather than `...Options` to avoid colliding with
`SentryOptions`, `SwaggerOptions` and `ForwardedHeadersOptions` from the frameworks.

**Do not use it when** the value is read once at composition time and never again. A
`configuration["..."]` lookup is honest there.

---

## Producer / Consumer

**What.** The API publishes, the Worker consumes. See
[ADR 0005](../adr/0005-rabbitmq-producer-consumer-publish-after-commit.md).

**Where.** `Infrastructure/Messaging/`, `Api/Endpoints/`, `Worker/Consumers/`.

**How.** A handler calls `IMessagePublisher.PublishAsync`; the foundation defers it until after
the commit. A consumer derives from `RabbitMqConsumerBase<T>`, declares its `QueueName` and
implements `HandleAsync`, and its message is registered in `MessageRoutes.CreateDefault()`.

**Idempotency is inherited**, not remembered — see
[ADR 0010](../adr/0010-consumer-idempotency.md).

**Rules that still apply to you:** messages carry ids, never user content. A handler must be
safe to run twice.

**Do not use it when** the work is fast enough to do inline. A message for something that takes
five milliseconds adds a queue, a retry policy and a failure mode for no gain.

---

## What is deliberately absent

These are not oversights; they were considered and rejected for this stage. Adding one means
writing an ADR first.

| Absent | Why |
|---|---|
| MediatR, AutoMapper, Scrutor | A dependency, a runtime failure mode or a licence we do not need. See [ADR 0002](../adr/0002-cqrs-lite-with-in-house-dispatcher.md). |
| Transactional outbox | The buffer gives the ordering guarantee without a table. Durability is a later decision. |
| Dead-letter queue | Three in-process attempts then acknowledge, with a final-failure hook. |
| Soft delete | Deleted rows complicate every query and every index for a feature nobody asked for. |
| Lazy loading | N+1 queries appear without a visible line of code causing them. |
| Rate limiting, audit tables | Later steps, with their own ADRs. |