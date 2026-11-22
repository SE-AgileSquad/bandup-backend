# Implementation status

The state of the foundation and implemented product slices, including verified behavior and
known limits. The detailed auth contract is in [ADR 0016](adr/0016-refresh-logout-contracts-and-commit-outcomes.md).

Last updated: 2026-11-22.

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
| C7 | `System` reference slice — server time and ping | **Done** |
| C8 | Local dependency stack, env example, reserved folders | **Done** |
| C9 | Testcontainers integration tests | **Done** |
| C10 | ADRs, patterns guide, architecture, feature recipe, run instructions | **Done** |

## Test suite

The suite includes foundation, Identity, signup Billing and refresh/logout acceptance checks.
The final 2026-11-17 verification passed **258 tests**, with zero failures or skips and a
Release build with zero warnings/errors.
The final branch verification commands and scope are recorded in
[the auth acceptance matrix](auth-session-acceptance.md).

Password-recovery phase 1 verification on 2026-11-20: the serial Release build completed
with zero warnings/errors; **133 unit tests and 11 architecture tests passed**, with no
failures or skips. Test execution required access outside the sandbox to the runner's
Windows named pipe. Integration tests were not rerun because Docker's Linux engine was
unavailable. This does not replace the earlier full refresh/logout acceptance run.

Password-recovery phase 2 verification on 2026-11-21: Release build completed with zero
warnings/errors; **173 unit, 11 architecture and 158 integration tests passed (342 total)**,
with zero failures/skips. Docker Desktop 28.3.2 was available for the disposable PostgreSQL,
Redis and RabbitMQ fixtures. The integration run applied the new migration and verified
replacement rollback, stale-state refresh, shared user locking, unique-index enforcement
and `xmin` conflict detection. Tests ran outside the sandbox for Windows pipe access.
The developer's database and user secrets were not changed.

Password-recovery phase 3 verification on 2026-11-22: Release build completed with zero
warnings/errors; **202 unit, 11 architecture and 168 integration tests passed (381 total)**,
with zero failures/skips. Docker Desktop 28.3.2 supplied the disposable dependencies, including
`axllent/mailpit:v1.27.0` for real local HTTP email capture. Tests verify request/resend,
unknown/inactive acknowledgement, save/commit failure rollback without delivery, atomic
Redis quotas, response timing, expiry handling and sanitized background-delivery failures.
The shared API fixture explicitly selects offline email defaults, independently of developer
email settings. Tests ran outside the sandbox for Windows pipe access. No developer database,
user secrets or production email provider were changed/configured. No new migration is needed.

Password-recovery phase 4 verification on 2026-11-22: Release build completed with zero
warnings/errors; **222 unit, 11 architecture and 181 integration tests passed (414 total)**,
with zero failures/skips. Tests verify committed incorrect-guess counters, expiry after lock
waits and hashing, one-use code consumption, atomic password/session changes, save/commit
rollback without notification, concurrent resets and reset/refresh serialization. Successful
reset is also checked against old/new password login and post-commit notification state.
Docker Desktop 28.3.2 supplied disposable dependencies. Tests ran outside the sandbox for
Windows pipe access. No developer database, user secrets or real email service were changed.
Recovery endpoints and cookie clearing remain phase 5, using local email; phase 6 connects
the real email service. No migration or new package is needed for this checkpoint.

| Project | Covers |
|---|---|
| `BandUp.UnitTests` | Pipeline semantics, domain rules, handlers, token issuance/revocation, CAPTCHA adapters and logging |
| `BandUp.ArchitectureTests` | Project dependency direction and framework bans |
| `BandUp.IntegrationTests` | Foundation scenarios, migrations, registration/login/profile, session HTTP flows, database locks, Redis failures and concurrent requests |

## Product slices

| Slice | Implemented behavior |
|---|---|
| Identity | Registration, password login, profile, rotating refresh, reuse detection and logout |
| Session transport | HttpOnly cookies with configured expiry/security, CSRF guard and common ProblemDetails |
| Access revocation | Validated JWT metadata and Redis blacklist enforcement through expiry plus clock skew |
| Billing signup | Credit account and ledger with the signup grant; `/me` reads the current balance |
| Auth developer flow | Fake CAPTCHA and Development Swagger header support; [setup guide](auth-local-setup.md) |
| Password recovery | Phases 1–4: contracts, challenge/storage/protection, request/resend, atomic quotas, timing, local email delivery and atomic reset with refresh revocation; [ADR 0017](adr/0017-email-password-reset-contracts-and-policy.md). Phase 5 is local HTTP/Swagger acceptance; phase 6 connects the real email service. |

## Scenario coverage

| ID | Scenario | Test |
|---|---|---|
| INF-01 | `/health` with all dependencies up | Integration |
| INF-02 | one dependency down returns 503 naming it | Integration |
| INF-03 | `/live` without dependencies; `/ready` withholds traffic while migrations are pending | Integration |
| INF-04 | unhandled exception → 500 with the request id, no stack trace | Integration |
| INF-05 | invalid `SendPing` → 400 with an `errors` map | Unit + Integration |
| INF-06 | `X-Request-Id` preserved, replaced, and present in logs | Integration |
| INF-07 | ping returns 202 and the Worker consumes it | Integration |
| INF-08 | layering rules hold | Architecture |
| INF-09 | pipeline order; transaction commits and rolls back | Unit + Integration |
| INF-10 | the extensions exist after `--migrate` | Integration |
| INF-11 | log lines are JSON with the required keys; sensitive values masked | Unit |
| INF-12 | CORS allows only the configured origin with credentials | Integration |
| INF-13 | the OpenAPI document lists the endpoints and their responses | Integration |
| AUTH-01 to AUTH-08 | Registration, CAPTCHA behavior, login timing and authenticated profile | Unit + Integration |
| AUTH-09 | Rotation, expired/inactive rejection, committed reuse revocation and concurrent refresh | Unit + Integration |
| AUTH-10 | Owner-scoped logout, JWT blacklist, expiry/skew, Redis failures and refresh/logout races | Unit + Integration |

## Approved adjustments

Each is a deliberate departure from, or extension of, the original plan.

| # | Adjustment | ADR | Code |
|---|---|---|---|
| 1 | `Application` may reference `Contracts` | [0008](adr/0008-application-references-contracts.md) | Done |
| 2 | Publishing is buffered and flushed after commit | [0009](adr/0009-transaction-aware-message-publishing.md) | Done |
| 3 | Consumer idempotency via a cache-backed handled set | [0010](adr/0010-consumer-idempotency.md) | Done |
| 4 | Topology declared at startup by both processes | [0011](adr/0011-topology-declared-by-both-processes.md) | Done |
| 5 | `PageRequest` added as the input counterpart to `PagedResult<T>` | — | Done |
| 6 | One shared JSON contract for cache, messaging and the API | — | Done |
| 7 | Enum `CHECK` constraint names snake-cased by the naming helper | — | Done |
| 8 | Two supporting packages: `Microsoft.EntityFrameworkCore.Relational`, `Microsoft.Extensions.Hosting.Abstractions` | — | Done |
| 9 | Test runner is xunit.v3 on Microsoft Testing Platform | — | Done |
| 10 | Sentry: `Sentry.AspNetCore` (API), `Sentry.Extensions.Logging` (Worker) | — | Done |
| 11 | Host ports in the compose file are configurable, with the standard ports as defaults | — | Done |

### Note on adjustment 8

`Npgsql.EntityFrameworkCore.PostgreSQL` requires `Relational >= 10.0.4`, while `Design` is
`PrivateAssets=all` and so does not lift the transitive version. Without an explicit pin the
build emits five `MSB3277` warnings, and warnings are errors here. `Hosting.Abstractions` is
required for `BackgroundService`, which the consumer base derives from.

### Note on adjustment 11

Windows reserves large blocks of TCP ports at boot for Hyper-V and WinNAT, which can make the
standard ports unbindable. The compose file reads `POSTGRES_PORT`, `REDIS_PORT`,
`RABBITMQ_PORT` and `RABBITMQ_MANAGEMENT_PORT`, defaulting to the standard values, so the port
can be moved without editing the file. See the README section "Ports that cannot be bound".

## Known gaps

| Gap | Impact | Where it closes |
|---|---|---|
| Sentry is verified only in its disabled state | The enabled path is untested | Deployment step, with a real DSN |
| SIGTERM specifically is unverified | Windows has no SIGTERM; Ctrl+C exercises the same cancellation path | Deployment step, on Linux |
| `INF_07` is not tagged `Slow` | It runs with the rest of the suite (~2 s) | When the suite grows enough to need a fast subset |
| Native browser rendering and real Turnstile interaction are not automated in this suite | HTTP cookie jars, Swagger assets and the fake provider are covered; a real widget needs client integration | Frontend/browser acceptance |
| Login JSON uses a placeholder credit balance | `/me` returns the actual balance | Separate review-fix branch |
| Register/login handlers currently omit the available client IP when invoking CAPTCHA | CAPTCHA outage quota can share the unknown-client bucket | Separate review-fix branch |
| Signup credit repository additions are not awaited | The existing implementation should await both repository operations | Separate review-fix branch |

## Session limits

Clients must serialize refresh calls and coordinate logout with any pending refresh, including
across tabs. A logout that presents an old cookie after a concurrent refresh does not revoke
the newly issued successor. Redis blacklist reads fail open on outage; writes fail logout.
PostgreSQL and Redis have no shared transaction, so a database failure after a blacklist write
can reject the access JWT while its refresh revocation rolls back. These are documented and
covered in [the client contract](auth-client-contract.md) and acceptance matrix.

## Not in this repository, by design

Frontend, Dockerfiles for the two applications, the production compose stack, Nginx, CI/CD and
deployment. The `infra/nginx/`, `infra/scripts/` and `.github/workflows/` folders exist only as
reserved placeholders.
