# 0015 — One schema per module, one migration per PR, sequential merges

**Status:** Accepted.

## Context

The product spec requires a PostgreSQL schema per module, and the codebase has a **single
`BandUpDbContext`** with a single model snapshot. Several people add entities at the same time,
each needing a migration. Two migrations generated in parallel from the same snapshot cannot
both be correct: the second is built against a model that does not include the first's tables,
so merging them produces a snapshot that disagrees with the database.

## Decision

- **One schema per module** (`identity`, `content`, `exams`, `learning`, `credits`, …). The
  schema is set explicitly in the entity configuration; nothing is inferred.
- **One migration per module per PR.** A migration is always generated from the tip of `main`
  after every other migration has merged.
- **Migrations merge sequentially, never in parallel.** Whoever is next rebases onto the merged
  migration and generates theirs on top. A migration is never edited after it has merged.
- **The first migration of a new schema is checked for `EnsureSchema`.** EF emits it
  automatically when a configuration calls `ToTable(name, schema)`, so no hand-edit is normally
  needed — but it is verified on the generated file rather than assumed, because a missing
  schema statement fails at apply time, not at generation time.
- **Cross-module reads go through Application interfaces**, never another module's tables. A
  foreign key across schemas is allowed only where the spec's ERD draws one.
- **Every migration is reviewed by hand before commit** — generated code guesses at renames and
  defaults, and the first table in a new schema reliably needs the schema statement added.

## Consequences

- Migration conflicts stop being a merge-time surprise and become a scheduling rule.
- A developer who needs an entity owned by another module gets an interface, not a table.
- Cost: contributors are serialised on migration creation. In practice this is minutes per
  module, and it is the only correct option with one snapshot.
- Cost: hand-editing generated migrations is sometimes needed here, so review attention is
  required on a file type that is usually skimmed.