# 0011 — Topology declared by both processes at startup

## Context

One `RabbitMqTopology` declares the exchange and queues, and the rule is that **both** the API
and the Worker call it. Declaring is idempotent, so overlapping calls are harmless.

In practice the API did not call it. The failure appeared immediately during verification: an
API-only run failed to publish with

```
NOT_FOUND - no exchange 'bandup.jobs' in vhost '/'
```

because nothing had created the exchange yet. The rule existed; the enforcement did not.

A second, smaller problem: the RabbitMQ health check had a 3-second budget, and the first probe
after startup could fail while the connection was still being established. An orchestrator that
treats a failed probe as a crash-loop signal would restart a perfectly healthy process.

## Decision

Register the declaration in Infrastructure, not in each host, so neither process can forget it.

- `RabbitMqTopologyInitializer` is an `IHostedService`. On start it opens the broker
  connection, declares the exchange and every queue in `MessageRoutes`, and logs the outcome.
- It is registered inside `AddInfrastructure`'s RabbitMQ branch, so **both** the API and the
  Worker get it with no per-process wiring, and a run with `MessagePublisher__Provider=InMemory`
  does not register it at all.
- It is registered before any consumer, so the exchange exists before consumption begins.
- The declaration also warms the connection, which removes the transient first-probe failure.
  The 3-second per-check timeout is unchanged.

If the broker is unreachable, the initializer logs the target host and port and rethrows, so the
process fails to start with a readable message instead of starting up unable to do its job.

## Consequences

- A publisher can never target an exchange that no process has created.
- The first health probe meets a warm connection, so the transient 503 is gone.
- Cost: **the API now requires RabbitMQ at startup.** It did not before. This follows from
  "both processes declare the topology", and it means a developer running the API without the
  dependency stack gets a clear failure rather than a deferred publish error.
- Cost: startup is coupled to the broker's availability. On a broker outage, restarts fail
  fast rather than starting in a degraded state. Accepted: a process that cannot publish
  cannot do its job anyway.
- Rejected: declaring lazily on first publish, which reintroduces the race the initializer
  removes.