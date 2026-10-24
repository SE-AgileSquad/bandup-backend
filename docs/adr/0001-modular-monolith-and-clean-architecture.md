# 0001 — Modular monolith with Clean Architecture

## Context

BandUp is an IELTS preparation platform with several distinct areas of behaviour (exams,
lessons, writing evaluation, an assistant, credits). The team is small and the whole product
ships on a single VPS. Splitting into services now would multiply deployment and debugging
cost without any proven scaling pressure.

At the same time, the areas are genuinely separable and a later extraction should not require
a rewrite. We also want the business rules to be testable without a database, a broker or an
HTTP host.

## Decision

Build one deployable API process, one Worker process and one PostgreSQL database, organised as
a **modular monolith** with **Clean Architecture** layering:

```
BandUp.Domain          depends on nothing
BandUp.Contracts       depends on nothing
BandUp.Application     -> Domain
BandUp.Infrastructure  -> Application, Domain, Contracts
BandUp.Api             -> Application, Infrastructure, Contracts
BandUp.Worker          -> Application, Infrastructure, Contracts
```

> **Amended by [0008](0008-application-references-contracts.md).** Application now also
> references Contracts, so a command handler can publish the message contract it produces.

Each module owns its own PostgreSQL schema and its own folder in every layer. Modules talk to
each other only through Application interfaces, never by reading another module's tables.

The layering is enforced by automated architecture tests, not by review discipline: a
`ProjectReference` that points the wrong way fails the build, and a type that reaches for
Entity Framework, ASP.NET Core, Redis or RabbitMQ from Domain or Application fails a test.

## Consequences

- One solution to build, one database to back up, one place to look when something breaks.
- Business rules are unit-testable with no infrastructure at all, because Domain and
  Application cannot reference it.
- A module can later be extracted into its own service: its boundary is already explicit.
- Cost: more projects and more ceremony than a single project would need. Every layer
  boundary is a file the author has to know about.
- Cost: cross-module queries cannot use a join. They go through an interface, which is
  slower to write but keeps the boundary honest.
