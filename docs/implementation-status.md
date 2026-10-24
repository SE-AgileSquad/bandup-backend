# Implementation status

Tracks what is built, what is not, and every place the implementation knowingly differs from
the original plan. Kept in the repository so the state of the foundation is readable without
reading the commit log.

Last updated: 2026-10-25.

## Checkpoints

| # | Scope | Status |
|---|---|---|
| C0 | Repository hygiene — `.gitignore`, `.gitattributes`, `.editorconfig` | **Done** |
| C1 | Solution, build props, nine projects | **Done** |
| C2 | Architecture tests (INF-08) | **Done** |
| C3 | Domain and Application building blocks, dispatcher, Result, behaviors | **Done** |
| C4 | Persistence, Redis cache, RabbitMQ messaging, initial migration | **Done** |
| C5 | API host — logging, error handling, health endpoints, OpenAPI | **Done** |
| C6 | Worker host, topology declaration, graceful shutdown | **Done** |
| C7 | `System` reference slice — server time and ping | **Not started** |
| C8 | Local dependency stack (Docker Compose), env example, reserved folders | **Not started** |
| C9 | Testcontainers integration tests (INF-01 – INF-07, 09, 10, 12, 13) | **Not started** |
| C10 | ADRs, patterns guide, container view, run instructions | **Partially done** — see below |

### C10 partial

Written early, because the decisions behind the code were worth recording while they were
fresh: the ADRs, the patterns guide, the container view, this file and the repository README.

Still owed at C10:

- `docs/how-to-add-a-feature.md` — deferred deliberately. It is a recipe that walks through
  copying the reference slice, so it cannot be written honestly until C7 exists.

## Feature status

The `System` slice is the only feature in the repository and it is not built yet. This is
expected: the foundation is intentionally built before the first feature, so that feature
follows a template rather than inventing one.

| Piece | Status |
|---|---|
| Domain entities | None — no product features in this stage |
| `Contracts` messages | None — the first message (`PingRequested`) arrives with C7 |
| API endpoints | One: `GET /api/_debug/throw`, diagnostics only |
| Worker consumers | None — `PingConsumer` arrives with C7 |

## Approved adjustments

Each of these is a deliberate departure from, or extension of, the original plan. Each has an
ADR recording the reasoning.

| # | Adjustment | ADR | Code |
|---|---|---|---|
| 1 | `Application` may reference `Contracts` | [0008](adr/0008-application-references-contracts.md) | **Pending — lands with C7** |
| 2 | Message publishing is buffered and flushed after commit | [0009](adr/0009-transaction-aware-message-publishing.md) | Done |
| 3 | Consumer idempotency via a cache-backed handled set | [0010](adr/0010-consumer-idempotency.md) | Done |
| 4 | Topology declared at startup by both processes | [0011](adr/0011-topology-declared-by-both-processes.md) | Done |
| 5 | `PageRequest` added as the input counterpart to `PagedResult<T>` | — | Done |
| 6 | One shared JSON contract for cache, messaging and the API | — | Done |
| 7 | Enum `CHECK` constraint names snake-cased by the naming helper | — | Done |
| 8 | Two supporting packages added: `Microsoft.EntityFrameworkCore.Relational`, `Microsoft.Extensions.Hosting.Abstractions` | — | Done |
| 9 | Test runner is xunit.v3 on Microsoft Testing Platform | — | Done |
| 10 | Sentry: `Sentry.AspNetCore` (API), `Sentry.Extensions.Logging` (Worker) | — | Done |

### Note on adjustment 8

`Npgsql.EntityFrameworkCore.PostgreSQL` requires `Relational >= 10.0.4`, while `Design` is
`PrivateAssets=all` and so does not lift the transitive version. Without an explicit pin the
build emits five `MSB3277` warnings, and warnings are errors here. `Hosting.Abstractions` is
required for `BackgroundService`, which the consumer base derives from.

## Known gaps

Honest list of things that are not covered, with where each will be closed.

| Gap | Closed by |
|---|---|
| Integration tests — INF-01 to INF-07, 09, 10, 12, 13 | C9 |
| `Application → Contracts` reference and the relaxed layering assertion | C7 |
| Consumer retry and backoff path is not exercised by a test | C9 |
| Shutdown-mid-delivery branch is not exercised by a test (no consumer yet) | C7 / C9 |
| Sentry is verified only in its disabled state (no DSN available) | Deployment step |
| SIGTERM specifically is unverified (Windows has no SIGTERM; Ctrl+C uses the same cancellation path) | Deployment step |
| The duplicate-handler guard throws, but is not covered by a test | C9 |
| `dotnet test` exits non-zero while a test project is empty | C9 |