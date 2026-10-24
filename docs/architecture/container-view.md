# Container view

What runs, and what it talks to. The API and the Worker are host processes, not containers:
they run with `dotnet run` and are only containerised in a later deployment step.

```mermaid
flowchart TB
    subgraph client["Clients"]
        FE["Frontend<br/>(separate repository)<br/>Next.js"]
        OP["Operator<br/>curl, Swagger UI"]
    end

    subgraph host["Host machine (dotnet run)"]
        API["<b>BandUp.Api</b><br/>ASP.NET Core minimal APIs<br/>/api, /live, /ready, /health, /swagger<br/>publishes jobs"]
        WORKER["<b>BandUp.Worker</b><br/>Generic host<br/>consumes jobs"]
    end

    subgraph deps["Local dependencies (Docker Compose)"]
        PG[("<b>PostgreSQL 17</b><br/>pgvector + citext<br/>schema per module")]
        REDIS[("<b>Redis 7</b><br/>bandup: prefix")]
        MQ[("<b>RabbitMQ 4</b><br/>exchange bandup.jobs<br/>type direct, durable")]
    end

    SENTRY["Sentry<br/>(optional —<br/>empty DSN disables)"]

    FE -->|"HTTPS, JSON<br/>X-Request-Id"| API
    OP -->|"HTTPS"| API
    API -->|"OpenAPI document<br/>generates the client"| FE

    API -->|"EF Core<br/>read + write"| PG
    WORKER -->|"EF Core<br/>write"| PG
    API -->|"ICacheService"| REDIS
    WORKER -->|"dedupe keys"| REDIS
    API -->|"publish, after commit"| MQ
    MQ -->|"manual ack,<br/>prefetch 1"| WORKER

    API -.->|"errors, traces"| SENTRY
    WORKER -.->|"errors"| SENTRY

    API -.->|"declares topology<br/>at startup"| MQ
    WORKER -.->|"declares topology<br/>at startup"| MQ
```

## Notes

**Both hosts declare the RabbitMQ topology at startup.** Declaring is idempotent, so the
overlap is harmless, and it guarantees the exchange exists before either process publishes or
consumes. See [ADR 0011](../adr/0011-topology-declared-by-both-processes.md).

**Only the API serves HTTP.** The Worker has no listening socket; its health is whether it is
consuming. It reports to Sentry and to stdout logs.

**The frontend is a separate repository.** It consumes this API through the generated OpenAPI
document; there is no shared code between them.

**One database, one schema per module.** Modules do not read each other's tables. See
[ADR 0004](../adr/0004-postgresql-pgvector-schema-per-module.md).

**Redis is a cache, not a source of truth.** Losing it costs a cold cache and a wider duplicate
window for consumers, not data. See [ADR 0010](../adr/0010-consumer-idempotency.md).

**Sentry is optional.** An empty `Sentry__Dsn` disables it, which is the local default.

## Request flow

A command, end to end:

```mermaid
sequenceDiagram
    participant C as Client
    participant A as Api
    participant D as Dispatcher
    participant H as Handler
    participant PG as PostgreSQL
    participant B as MessageBuffer
    participant Q as RabbitMQ

    C->>A: POST /api/system/ping
    A->>A: RequestIdMiddleware — assign X-Request-Id
    A->>D: SendAsync<SendPing, Guid>
    D->>D: Logging behavior
    D->>D: Validation behavior
    Note over D: invalid → 400, handler never runs
    D->>D: Transaction behavior — BEGIN
    D->>H: HandleAsync
    H->>Q: PublishAsync
    B-->>B: buffered — transaction is open
    H-->>D: Result.Success
    D->>PG: SaveChanges
    D->>D: COMMIT
    D->>B: Flush
    B->>Q: publish (mandatory, persistent, messageId + attempt)
    D-->>A: Result.Success
    A-->>C: 202 + X-Request-Id
```

A rollback discards the buffer instead of flushing it, so the consumer never sees the message.
See [ADR 0009](../adr/0009-transaction-aware-message-publishing.md).