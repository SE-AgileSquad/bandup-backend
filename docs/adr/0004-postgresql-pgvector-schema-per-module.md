# 0004 — PostgreSQL with pgvector, one schema per module

## Context

The product needs relational data (users, exams, attempts, credits), full-text-ish lookups,
and vector similarity for the assistant and semantic search. It also needs to stay operable by
a small team on a single VPS.

Running a separate vector database next to a relational one would double the operational
surface and force us to keep two copies of the content in sync, with no transaction spanning
them.

## Decision

Use **one PostgreSQL 17 database** with the `pgvector` and `citext` extensions.

- `vector` for embeddings, so a similarity search and the relational filter around it happen
  in the same query and the same transaction.
- `citext` for case-insensitive email comparison without lowercasing at every call site.
- **One schema per module.** Modules do not read each other's tables; they call Application
  interfaces.
- `snake_case` for tables, columns, keys, indexes and constraints, so hand-written SQL stays
  unquoted and predictable.
- Primary keys are GUIDs generated in the application with `Guid.CreateVersion7()`, never by
  the database. Time-ordered ids keep index locality and let an id be created before a round
  trip.
- `timestamptz` in UTC. Enums stored as text with a `CHECK` constraint. Optimistic concurrency
  through the system column `xmin` for contested rows. No soft delete. No lazy loading.

The first migration does nothing but enable the two extensions.

## Consequences

- One backup, one connection string, one thing to monitor.
- Semantic search can join against relational filters and participate in a transaction.
- The module boundary is visible in the schema, which makes a later extraction mechanical.
- Cost: pgvector indexes need tuning per workload, and an index build on a large table is a
  maintenance operation we will have to schedule.
- Cost: a single database is a single point of failure. Accepted for this stage; the parent
  plan places backups and replication in the deployment step.
- Rejected: a dedicated vector database, for operational cost and sync complexity. Rejected:
  database-generated ids, because they cannot be known before the insert.
