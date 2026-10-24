# 0009 — Transaction-aware message publishing

## Context

The rule "publish only after the database commit" (see [0005](0005-rabbitmq-producer-consumer-publish-after-commit.md))
conflicts with the natural way a handler is written. `TransactionBehavior` runs:

```
begin -> handler -> SaveChanges -> commit
```

A handler that calls `publisher.PublishAsync(...)` therefore publishes **inside** the
transaction. A consumer can then read a row that is not yet visible — or that is rolled back
a moment later. The symptom is intermittent and expensive to diagnose.

## Decision

Keep handler code natural and enforce the ordering in the foundation.

- `IMessagePublisher` is unchanged. Its documentation still states the guarantee, and the
  guarantee is now true.
- `BufferedMessagePublisher` decorates the transport publisher. While a transaction is open it
  queues the publish; otherwise it forwards immediately.
- `IMessageBuffer` holds the queued publishes for the current scope. `TransactionBehavior`
  opens it after `BeginTransaction`, **discards** it on a failed result or an exception, and
  **flushes** it after `CommitTransactionAsync` succeeds.
- The queued delegate keeps the message's generic type, so no reflection is needed at flush.
- The transport publisher sits behind `IDirectMessagePublisher` so the decorator can reach it
  without ambiguity. Both provider modes are wrapped, so tests get the same ordering guarantee.

This is **not** a transactional outbox: nothing is persisted, and nothing survives the process.
It is an ordering guarantee, not a durability guarantee.

## Consequences

- A consumer can never observe a row that was rolled back, because the message does not exist
  until after the commit.
- Handlers stay readable: they publish, and the framework does the right thing.
- The guarantee holds identically for RabbitMQ and for the in-memory publisher, so a unit test
  catches a regression.
- Cost: three small types (`IMessageBuffer`, `MessageBuffer`, `BufferedMessagePublisher`) plus
  one extra interface. This is the most subtle part of the foundation.
- Cost: a crash between commit and flush loses the message. Closing that gap needs an outbox
  table, which is a later decision behind the same interface.
