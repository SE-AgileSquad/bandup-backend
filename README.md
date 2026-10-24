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

Start with **[docs/architecture/patterns.md](docs/architecture/patterns.md)** to see how a
feature is written, and **[docs/implementation-status.md](docs/implementation-status.md)** for
what is built so far.

## Prerequisites

- **.NET 10 SDK** — the repository pins the SDK in `global.json`; a .NET 10 feature band is
  required. A newer major version is not accepted.
- **Docker** — for the local dependency stack, and separately for the integration tests.
- **Node.js** is *not* required; the frontend is a separate repository.

## Start the local dependencies

PostgreSQL (with pgvector and citext), Redis and RabbitMQ run in Docker. The API and the Worker
run on the host, not in containers.

> The compose file and the environment example referenced below are added by the infrastructure
> checkpoint and are not in the repository yet. Everything after this section is already in
> place.

Create your local environment file from the example and fill in the placeholders:

```bash
cp infra/env/dev.env.example infra/env/dev.env
```

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

Run these from the repository root. Replace the placeholders with the same values you put in
`infra/env/dev.env`.

```bash
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Port=5432;Database=bandup;Username=postgres;Password=CHANGE_ME" --project src/BandUp.Api
```

```bash
dotnet user-secrets set "RabbitMq:Password" "CHANGE_ME" --project src/BandUp.Api
```

```bash
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Port=5432;Database=bandup;Username=postgres;Password=CHANGE_ME" --project src/BandUp.Worker
```

```bash
dotnet user-secrets set "RabbitMq:Password" "CHANGE_ME" --project src/BandUp.Worker
```

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
| `/api/system/time` | **Not built yet** — arrives with the reference slice. |
| `/api/system/ping` | **Not built yet** — arrives with the reference slice. |

The two `_debug` and `system` routes require `Features__Diagnostics=true` and are never mapped
in Production. See [docs/implementation-status.md](docs/implementation-status.md) for what is
built so far.

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
  BandUp.UnitTests/       Application/, Logging/
  BandUp.ArchitectureTests/
  BandUp.IntegrationTests/
docs/
  adr/                    Architecture decision records
  architecture/           Patterns guide, container view
infra/
  docker/                 Local dependency stack
  env/                    Environment file example
```

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