# 0003 — Result pattern for expected failures

## Context

Most failures in this application are expected: a validation rule is broken, a row does not
exist, a user is not allowed, a submit conflicts with an earlier one. If these are modelled as
exceptions, three things go wrong: the method signature hides that they can happen, every
caller needs a `try`/`catch` to distinguish them from bugs, and the pipeline cannot inspect
the outcome without catching.

We also need one place that turns an outcome into an HTTP status, so the API stays consistent.

## Decision

Handlers return `Result` or `Result<T>`, carrying an `Error(Code, Message, ErrorType)` on
failure.

- **Expected failures are returned, never thrown.** Validation, not-found, conflict,
  forbidden, unauthorised.
- **Exceptions mean a bug or an infrastructure failure** and surface as `500`.
  `DomainException` specifically marks a violated invariant — a programming error.
- `ErrorType` maps to HTTP status in exactly one place. The same map produces the RFC 7807
  `type` URI (`https://bandup.app/errors/<code-in-kebab-case>`).
- Validation failures carry an `errors` map keyed by property name.
- `Result` is constructed through `Success`/`Failure`, which throw if you build a success
  carrying an error or a failure with no error. That guard protects the type itself; it is not
  a runtime error path.

## Consequences

- A handler's signature tells the truth: `Task<Result<T>>` says this can fail, without
  exceptions.
- `TransactionBehavior` reads `result.IsFailure` and rolls back — no exception plumbing.
- Endpoints map the result to HTTP in two lines, and every failure looks the same on the wire.
- Cost: callers must check `IsSuccess` before touching `.Value`, which throws if they do not.
  That is deliberate: returning `default` would hide the bug instead.
- Cost: the pipeline cannot use exceptions for control flow, so "log and rethrow" patterns are
  limited to genuine faults.

For refresh-token reuse, [ADR 0016](0016-refresh-logout-contracts-and-commit-outcomes.md)
defines a completed revocation outcome carried by a successful command result. The endpoint
rejects refresh only after that security action has committed; ordinary failed results still
roll back.
