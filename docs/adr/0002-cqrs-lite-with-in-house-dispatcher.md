# 0002 — CQRS-lite with an in-house dispatcher

## Context

Endpoints need to reach use-case handlers without the HTTP layer knowing which handler exists.
A mediator library (MediatR) solves this off the shelf, but it brings a third-party pipeline
model, an extra dependency to track, and — for this project — no capability we cannot write
ourselves in about seventy lines.

We also need cross-cutting concerns (logging, validation, transaction control) applied
uniformly, and we need "the transaction applies to commands only" to be a property of the
design rather than something each handler remembers.

## Decision

Split requests into **commands** and **queries**, both plain `record`s, and dispatch them
through a hand-written `IDispatcher`:

- `ICommand<TResponse>` / `IQuery<TResponse>` are markers carrying the response type.
- `ICommandHandler<,>` / `IQueryHandler<,>` return `Result<TResponse>`.
- `IPipelineBehavior<,>` wraps the handler. The chain is built by hand in `Pipeline`, in
  registration order, outermost first.
- Behaviors run in this order: **Logging, Validation, Transaction**.
- `TransactionBehavior` additionally implements `ICommandOnlyPipelineBehavior`, a marker the
  dispatcher uses to exclude it from the query pipeline.
- Handlers, validators and behaviors are registered by a hand-written assembly scan. A second
  handler for the same request type throws at startup, naming both types.

No mediator library. No Scrutor.

## Consequences

- The pipeline is about 165 lines in total, written once, and fully debuggable: a breakpoint
  in `Dispatcher` shows the exact behavior chain for a request.
- "Transaction applies to commands only" is structural. A query cannot accidentally open a
  transaction, because the behavior is filtered out before the chain is built.
- Duplicate handlers fail at startup instead of silently resolving to whichever registration
  won.
- Cost: call sites must state both type arguments — `SendAsync<SendPing, Guid>(...)` — because
  C# cannot infer `TResponse` from the `where TCommand : ICommand<TResponse>` constraint.
- Cost: a new developer has to learn our dispatcher instead of recognising MediatR. The
  handler shape is nearly identical, so the transfer is small.
- Rejected: MediatR, for the extra dependency and the vendor's licence change. Rejected:
  resolving handlers by reflection over `request.GetType()`, which loses compile-time safety
  for no benefit at this size.
