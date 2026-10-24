# 0008 — Application references Contracts

**Status:** Accepted. The code change lands with the `System` reference slice.

## Context

The original layering (see [0001](0001-modular-monolith-and-clean-architecture.md)) allowed
Application to reference Domain only. That holds until a command handler has to publish a
message, because message contracts live in `BandUp.Contracts`.

The reference slice makes the conflict concrete: the `SendPing` handler must publish
`PingRequested`, and `PingRequested` is a Contracts type. Under the original rule, Application
cannot see it.

Three options were considered:

1. Let Application reference Contracts.
2. Move the message into Domain.
3. Keep an Application-side message shape and map it to the Contracts DTO in Infrastructure.

## Decision

**Allow `BandUp.Application` to reference `BandUp.Contracts`.**

`BandUp.Contracts` depends on nothing — no EF, no ASP.NET, no Redis, no RabbitMQ — so this adds
no cycle and no framework coupling to Application. A message DTO is a shared contract by
definition, which is why it has its own project.

The architecture test that asserted "Application references nothing but Domain" is relaxed to
permit Contracts, and the exact-reference assertion is updated to match.

## Consequences

- A command handler can publish its message directly. No mapping layer, no duplicate type.
- The Worker and the API already reference Contracts, so both sides of a message see the same
  type — which was the point of the separate project.
- Cost: the "Application depends only on Domain" statement in 0001 is no longer literally
  true. This record supersedes it for that one edge.
- Rejected: option 2, because it pulls serialization-facing types into the domain model.
- Rejected: option 3, because it costs two types and a mapper per message, forever, and buys
  nothing — Contracts is already dependency-free.
