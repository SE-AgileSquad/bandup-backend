# 0010 — Consumer idempotency through a cache-backed handled set

## Context

RabbitMQ delivers **at least once**. There is no dead-letter queue in this stage, so a message
that fails three times in-process is acknowledged and gone — but a message is also redelivered
after a consumer crash, and a lost acknowledgement produces a duplicate.

"Consumers must be idempotent" was previously a rule in a document that every consumer author
had to remember. A rule that lives only in a document is not a guarantee.

## Decision

Move the mechanism into the consumer base class, so a consumer inherits it.

- `RabbitMqConsumerBase<T>` reads the `messageId` header.
- Before handling, it checks the cache for `consumed:{queue}:{messageId}`. A hit means the
  message was already handled, so it is acknowledged without running the handler.
- After a **successful** handle, and before the acknowledgement, it records the id with a
  **7-day** TTL.
- A final failure does **not** record the id, so a later redelivery can retry.

The mechanism is deliberately **mark-after-success, not claim-before-handle**. A pre-claim
would be wrong here: if the process died mid-handler the claim would already exist, the
redelivery would be skipped, and the message would be silently lost — turning at-least-once
into effectively at-most-once.

If a delivered body cannot be deserialised, the message is rejected without requeue rather than
throwing out of the consumer.

## Consequences

- A crash after handling but before the acknowledgement no longer causes a second execution.
- Consumers get this by inheritance. There is nothing to remember and nothing to re-implement.
- Cost: a narrow window remains where two copies of the same message are handled concurrently —
  across processes, or after a final-failure acknowledgement. `PrefetchCount` is 1 per channel,
  which closes the single-process case. This narrows the duplicate window; it does not justify
  dropping the rule that handlers should be idempotent by design.
- Cost: it depends on the cache being available. With Redis down, deduplication degrades rather
  than blocking consumption — the failure is a duplicate, not a stall.
- Rejected: claiming the id before handling, for the lost-message failure mode above.