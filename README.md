# bandup-backend

Backend for BandUp, an IELTS Reading and Writing preparation platform.

A **modular monolith** on .NET 10: one API process, one Worker process, one PostgreSQL
database, with Redis for caching and RabbitMQ for asynchronous work. Business logic lives in
the Domain and Application layers, which reference no framework at all — the layering is
enforced by automated tests, so a wrong-direction reference fails the build rather than a
review.

This repository holds the **foundation** and one reference slice. Product features are built
on top of it in later stages.

## What is here

| Project | Responsibility |
|---|---|
| `BandUp.Domain` | Entities, value objects, pure domain logic. Depends on nothing. |
| `BandUp.Contracts` | Message DTOs shared by the API and the Worker. Depends on nothing. |
| `BandUp.Application` | Use cases, interfaces, DTOs, validators, pipeline behaviors. |
| `BandUp.Infrastructure` | EF Core, Npgsql, Redis, RabbitMQ, logging, configuration. |
| `BandUp.Api` | HTTP composition root. Endpoints, middleware, health, OpenAPI. |
| `BandUp.Worker` | Background composition root. Consumes the job queues. |

Tests live in `tests/`: unit, architecture, and integration (Testcontainers).

## Where to read next

| Document | Read it when |
|---|---|
| [docs/architecture/backend-architecture.md](docs/architecture/backend-architecture.md) | you want the whole system on one page — the six projects, a request end to end, the contracts |
| [docs/how-to-add-a-feature.md](docs/how-to-add-a-feature.md) | you are about to write a feature |
| [docs/architecture/patterns.md](docs/architecture/patterns.md) | you want to know why a pattern is there, or when not to use it |
| [docs/adr/](docs/adr/) | you want the reasoning behind a decision |
| [docs/implementation-status.md](docs/implementation-status.md) | you want to know what is built and what is not |
| [docs/admin-users-api.md](docs/admin-users-api.md) | you are connecting the super-admin Users page |
| [docs/admin-dashboard-api.md](docs/admin-dashboard-api.md) | you are connecting the Admin home counters |
| [docs/admin-passages-api.md](docs/admin-passages-api.md) | you are connecting the admin Passages list and card actions |
| [docs/admin-lessons-api.md](docs/admin-lessons-api.md) | you are connecting the admin Lessons list, filters and archive action |
| [docs/admin-writing-prompts-api.md](docs/admin-writing-prompts-api.md) | you are connecting the admin Writing Prompts list and editor |
| [infra/README.md](infra/README.md) | you want to deploy, or change Docker/Nginx/CI/CD |
| [CONTRIBUTING.md](CONTRIBUTING.md) | you are about to branch, commit, or open a PR |

## Prerequisites

- **.NET 10 SDK** — the repository pins the SDK in `global.json`; a .NET 10 feature band is
  required. A newer major version is not accepted.
- **Docker** — for the local dependency stack, and separately for the integration tests.
- **Node.js** is *not* required; the frontend is a separate repository.

## Start the local dependencies

PostgreSQL (with pgvector and citext), Redis and RabbitMQ run in Docker. The API and the Worker
run on the host, not in containers.

Create your local environment file from the example and fill in the placeholders:

```bash
cp infra/env/dev.env.example infra/env/dev.env
```

The stack publishes PostgreSQL on 5432, Redis on 6379, RabbitMQ on 5672 and its management UI
on 15672. If any of those is already taken on your machine, set the matching `*_PORT` variable
in `dev.env` instead of editing the compose file. See
[Ports that cannot be bound](#ports-that-cannot-be-bound) below — on Windows this is common.

Then start the stack and wait for every health check to pass:

```bash
docker compose --env-file infra/env/dev.env -f infra/docker/docker-compose.dev.yml up -d --wait
```

Stop it, keeping your data:

```bash
docker compose --env-file infra/env/dev.env -f infra/docker/docker-compose.dev.yml stop
```

Stop it **and discard the data** — the fastest way back to a clean database:

```bash
docker compose --env-file infra/env/dev.env -f infra/docker/docker-compose.dev.yml down -v
```

## Configure secrets

Passwords do not live in the repository. The API and the Worker each read them from .NET user
secrets, which are stored outside the checkout.

Run these from the repository root. The username and password must be the same values you put
in `infra/env/dev.env`, and the port must match the one the stack is published on.

```bash
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Port=5432;Database=bandup;Username=bandup;Password=CHANGE_ME" --project src/BandUp.Api
```

```bash
dotnet user-secrets set "RabbitMq:Password" "CHANGE_ME" --project src/BandUp.Api
```

```bash
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Port=5432;Database=bandup;Username=bandup;Password=CHANGE_ME" --project src/BandUp.Worker
```

```bash
dotnet user-secrets set "RabbitMq:Password" "CHANGE_ME" --project src/BandUp.Worker
```

If you moved a port, also override the matching setting — for both projects, because both
connect to the same services:

```bash
dotnet user-secrets set "Redis:ConnectionString" "localhost:REDIS_PORT" --project src/BandUp.Api
```

```bash
dotnet user-secrets set "RabbitMq:Port" "RABBITMQ_PORT" --project src/BandUp.Worker
```

## Ports that cannot be bound

Windows reserves large blocks of TCP ports at boot for Hyper-V and WinNAT. A port inside one of
those ranges cannot be published by Docker, and the failure is easy to misread as a Docker
problem:

```
Error response from daemon: ports are not available: exposing port TCP 127.0.0.1:6379
-> 127.0.0.1:0: listen tcp4 127.0.0.1:6379: bind: An attempt was made to access a socket
in a way forbidden by its access permissions.
```

Check whether a port is affected:

```bash
netsh interface ipv4 show excludedportrange protocol=tcp
```

If it is, set the matching variable in `infra/env/dev.env` and use the same port in the user
secrets above. The ranges shift after a reboot, so a port that works today may not tomorrow —
picking one well outside the common ranges avoids the surprise.

```bash
REDIS_PORT=16379
RABBITMQ_PORT=15673
```

The management UI stays on 15672 in that example, since it is rarely among the reserved ports.
Nothing in the application hard-codes these ports; they come from configuration.

Configuration is validated at startup, so a missing value stops the process with a message
naming the key. It never surfaces later as a null reference.

## Apply migrations

Migrations are **never** applied automatically on startup. See
[ADR 0007](docs/adr/0007-migrations-applied-by-explicit-command.md).

```bash
dotnet run --project src/BandUp.Api -- --migrate
```

Exits `0` on success and non-zero on failure. Until this has run, the API starts but `/ready`
reports unhealthy.

## Run the API

```bash
dotnet run --project src/BandUp.Api
```

Listens on `http://localhost:8080`.

| Endpoint | Purpose |
|---|---|
| `/live` | Process is up. No dependency checks. |
| `/ready` | Dependencies reachable **and** no pending migrations. Gates traffic. |
| `/health` | Detailed status of PostgreSQL, Redis and RabbitMQ. |
| `/swagger` | Swagger UI, in Development. |
| `/api/_debug/throw` | Deliberate failure, to exercise the error format. Diagnostics only. |
| `/api/system/time` | Reference slice — server time, environment and version. Diagnostics only. |
| `/api/system/ping` | Reference slice — publishes a job the Worker consumes. Diagnostics only. |

The `_debug` and `system` routes require `Features__Diagnostics=true` and are never mapped in
Production, even when the flag is set.

A quick check that the API and the Worker are talking:

```bash
curl -X POST localhost:8080/api/system/ping -H 'Content-Type: application/json' -d '{"message":"hi"}'
```

The API returns `202` with a `pingId`, and the Worker logs `Received ping <pingId>`.

## Run the Worker

```bash
dotnet run --project src/BandUp.Worker
```

Declares the RabbitMQ topology at startup, then consumes. Shuts down gracefully: a message
being handled is finished, and anything still in flight is left unacknowledged for the broker
to redeliver.

## Run the tests

```bash
dotnet test
```

The integration tests start their own containers, so they do not use — and do not disturb —
your local dependency stack. Docker must be running for those; everything else runs offline.

Filter to a single project or test:

```bash
dotnet test tests/BandUp.UnitTests
```

## Folder map

```
src/
  BandUp.Domain/          Common/, and per-module folders as features arrive
  BandUp.Contracts/       Messages/
  BandUp.Application/     Abstractions/, Common/, Behaviors/, Features/<Module>/
  BandUp.Infrastructure/  Persistence/, Caching/, Messaging/, Logging/, Configuration/
  BandUp.Api/             Endpoints/<Module>/, Health/, Middleware/, Errors/
  BandUp.Worker/          Consumers/
tests/
  BandUp.UnitTests/       Application/, Features/, Logging/
  BandUp.ArchitectureTests/
  BandUp.IntegrationTests/  Infrastructure/ (fixtures), and one file per scenario group
docs/
  adr/                    Architecture decision records
  architecture/           Whole-system view and patterns guide
  how-to-add-a-feature.md Recipe for a new feature
  implementation-status.md  What is built, and what is not
infra/
  docker/                 Local dependency stack, and the full production stack
  env/                    Environment file examples (dev and prod)
  nginx/                  Reverse-proxy config, served by the prod stack
  scripts/                Server setup and deploy scripts
```

## Deployment

CI (build + test) and CD (build images, deploy over SSH) run in `.github/workflows/`. The full
production Docker Compose stack, Nginx config and server setup script live under `infra/`. See
[infra/README.md](infra/README.md) for the one-time server setup and the deploy/rollback steps.

## Conventions worth knowing

- **Expected failures are returned, not thrown.** Handlers return `Result`. Exceptions mean a
  bug or an infrastructure fault. See [ADR 0003](docs/adr/0003-result-pattern.md).
- **Message publishing happens after the commit**, enforced by the foundation — you do not
  have to remember it. See [ADR 0009](docs/adr/0009-transaction-aware-message-publishing.md).
- **Consumers are idempotent by inheritance**, not by discipline. See
  [ADR 0010](docs/adr/0010-consumer-idempotency.md).
- **Time comes from `TimeProvider`**, never `DateTime.Now` or `DateTime.UtcNow`.
- **Every async method takes a `CancellationToken`.**
- **Business messages carry ids, never user content.**
- No secrets in the repository. Configuration comes from environment variables or user secrets.
