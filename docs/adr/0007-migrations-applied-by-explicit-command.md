# 0007 — Migrations applied by an explicit command

## Context

Applying migrations automatically on API startup is convenient and dangerous. With more than
one instance, several processes race to migrate the same database. A failed migration takes
the process down in a crash loop. And a schema change is then coupled to a deploy, so a
rollback of the application also rolls back the database.

## Decision

Migrations are applied by an explicit command, never on startup:

```
dotnet run --project src/BandUp.Api -- --migrate
```

It applies pending migrations and exits `0`, or exits non-zero on failure. A connection-string
validation failure surfaces here with a readable message rather than as a null reference.

To keep a process from serving traffic against a stale schema, `/ready` includes a
**pending-migrations** health check. It is not part of `/health`, because it gates traffic
rather than describing the health of a dependency.

Migrations live in `Persistence/Migrations`. The history table is `__EFMigrationsHistory` in
the `public` schema, not in a module schema.

## Consequences

- Migration is a deliberate, observable step with a clear exit code — suitable for a deploy
  pipeline later.
- Multiple instances cannot race: they do not migrate at all.
- Rolling back the application does not silently roll back the schema.
- Cost: a deploy has one more step, and running it twice is the operator's responsibility
  (idempotent in practice, but still an ordering constraint).
- Cost: forgetting the step does not break the API immediately — it shows up as a failed
  `/ready` and a missing table at first use.
