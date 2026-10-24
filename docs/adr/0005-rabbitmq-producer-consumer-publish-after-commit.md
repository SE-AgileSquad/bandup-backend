# 0005 — RabbitMQ producer/consumer, publish only after commit

## Context

Some work must not block an HTTP response: evaluating a writing submission, indexing content
for search. That work needs to survive a restart and to be retried when a dependency is
briefly unavailable.

The dangerous case is a message that reaches a consumer before the database transaction that
produced it has committed. The consumer then reads a row that does not exist yet — or worse,
one that is rolled back moments later — and the failure is intermittent and hard to reproduce.

## Decision

Use **RabbitMQ 4** through `RabbitMQ.Client` v7 (fully async).

- Exchange `bandup.jobs`, type `direct`, durable. Queues durable. The routing key is the queue
  name.
- Messages are persistent JSON carrying a `messageId` header and an `attempt` header.
  Publisher confirms are on.
- **Business messages carry ids only, never user content.** The diagnostic `PingRequested` is
  the single exception.
- **Publish only after the database transaction has committed.** Handlers call
  `IMessagePublisher` normally; a decorator (`BufferedMessagePublisher`) holds the message in
  a scope-lived buffer while a transaction is open and releases it only after the commit. A
  rollback discards the buffer. See [0009](0009-transaction-aware-message-publishing.md).
- Consumers use manual acks with prefetch 1. A handler that keeps failing is retried in
  process (2 s, 10 s, three attempts), then handed to a final-failure hook and acknowledged.
  A process crash never acknowledges, so the broker redelivers.
- There is **no dead-letter queue yet**, so consumers must be idempotent. See
  [0010](0010-consumer-idempotency.md).
- One idempotent `RabbitMqTopology` declares the exchange and queues. Both processes call it
  at startup. See [0011](0011-topology-declared-by-both-processes.md).

## Consequences

- A slow evaluation never holds an HTTP request open, and survives a Worker restart.
- The publish-after-commit rule is enforced by the foundation, so a handler cannot get it
  wrong by forgetting it.
- Cost: without an outbox, a crash between commit and flush loses the message. Accepted for
  this stage; the buffer is deliberately not durable, and adding an outbox later is a
  contained change behind the same interface.
- Cost: no DLQ means a permanently failing message is acknowledged and lost after three
  attempts, with only the final-failure hook to record it.
- Rejected: publishing from the endpoint after dispatch. It needs no new code, but it pushes
  an infrastructure invariant onto every endpoint author.
