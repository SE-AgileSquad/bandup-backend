# 0017 — Email password-reset contracts and policy

**Status:** Phases 1–3 implemented: contracts/validation, the challenge lifecycle, PostgreSQL
storage, code protection, request/resend handler, atomic quotas and post-commit local email
delivery. Password-reset execution and HTTP routes remain in phases 4–5. Password recovery
is not available in Swagger yet. A production delivery provider remains to be selected.

## Context

AUTH-15 extends Identity with email codes for forgotten-password recovery. Registration,
password hashing, `User.ChangePassword`, refresh-session revocation and the transaction
pipeline already exist. This feature uses them without issuing a login session after reset.

As with [refresh reuse](0016-refresh-logout-contracts-and-commit-outcomes.md), an ordinary
failed `Result` rolls back database writes. Incorrect-code counters must survive rejection.
Domain and Application must remain independent of HTTP, EF Core and email-provider SDKs.

## Decision

### Planned HTTP contract

Both routes are anonymous and accept `application/json`. Email is the account identifier;
an existing access/refresh cookie is not proof that a reset code is valid.

| Route | JSON | Success | Expected rejection |
|---|---|---|---|
| `POST /api/auth/forgot-password` | `{ "email": "learner@example.com" }` | `202` with `ForgotPasswordOutcome`'s generic message | `400 VALIDATION_ERROR`; client-IP-wide throttling uses `429 PASSWORD_RESET_RATE_LIMITED` |
| `POST /api/auth/reset-password` | `{ "email": "learner@example.com", "code": "012345", "newPassword": "ExamplePass123!" }` | Empty `204`; clear current browser's session cookies after commit | `400 VALIDATION_ERROR`; generic `422 INVALID_PASSWORD_RESET_CODE`; client-IP-wide throttling uses `429 PASSWORD_RESET_RATE_LIMITED` |

- Missing/invalid email, malformed code and a password outside the existing password policy
  are structural validation errors. Structurally valid codes still require verification in
  the handler; validators never look up an account or a challenge.
- Registration and reset share `PasswordPolicy`: 10–128 characters with at least one letter
  and one digit, preserving registration's existing policy. Passwords are not trimmed.
- Codes are strings containing exactly six ASCII digits. Preserve leading zeroes and reject
  whitespace, extra characters and non-ASCII digits rather than normalizing credentials.
- Unknown/inactive users, absent challenges, wrong/expired/consumed/superseded codes and
  exhausted account-specific verification budgets all produce the same public reset error.
  A reset-code rejection is `422`, rather than an access-token `401` that could trigger the
  frontend's refresh-and-retry behavior.
- Forgot-password's public response never identifies account existence, cooldown state,
  delivery success or remaining per-account quota. Per-email suppression also returns `202`.
  Match observable work/timing for eligible and ineligible accounts. Do not return challenge
  ids, code values, attempt counts or account-specific retry headers.
- Client IP comes from `HttpContext.Connection.RemoteIpAddress` under the existing trusted
  proxy policy, not a JSON field or an independently trusted forwarding header. Missing IP
  uses one bounded shared unknown-client bucket, never an exemption.
- Keep the existing JSON and `X-Requested-With: bandup` CSRF requirements whenever session
  cookies accompany these requests. Failures do not change cookies. An infrastructure
  failure returns generic `500` and never claims the password was changed.

### Initial limits

`PasswordResetPolicy` is the source of these values. Phase 3 implements atomic Redis quotas
and a process-local in-memory fake; the generic cache's read-then-write API is not used.

| Rule | Value |
|---|---|
| Code lifetime | 10 minutes |
| Incorrect attempts per challenge | 5; the fifth wrong guess exhausts the challenge |
| Resend cooldown per email | 60 seconds |
| Accepted send requests per email | 5 per hour |
| Request attempts admitted by the client-IP gate | 20 per hour, including subsequent email-specific suppression |
| Verification attempts per email, across replacements | 20 per 15 minutes |
| Verification attempts per client IP, across emails/replacements | 100 per 15 minutes |

Windows are fixed UTC windows. Quotas count requests independently of account eligibility;
email keys follow the repository's case-insensitive email lookup. Use opaque keyed hashes
for email/IP bucket keys, not raw addresses in Redis keys or logs. Resending does not reset
window budgets. Account-specific limits affect recovery only and do not deactivate the user
or block normal password login. IP-wide limits are evaluated before account-specific checks
and may safely use a uniform `429` across account states.

A permitted replacement invalidates the previous code. Suppressed requests do not invalidate
an otherwise usable challenge. A code is usable only while `now < ExpiresAt`, before the
attempt limit, and while unused/unsuperseded. Generate it with cryptographic randomness.

### Protection, storage and delivery

- Persist the challenge in PostgreSQL so password changes and code consumption can commit
  together. Store a purpose- and challenge-bound HMAC of the code with a separate secret;
  never persist the plaintext code. An unkeyed refresh-token-style SHA-256 hash does not
  adequately protect the small six-digit search space against offline guessing.
- Use constant-time MAC comparison. Future configuration must fail clearly if the code
  protection secret or selected real email-provider credentials are missing. Phase 2 adds
  `PasswordReset:CodeProtectionKey`, validated when code protection is resolved. The existing
  API/Worker do not resolve it yet; the HTTP phase will enable API startup validation when
  recovery becomes available. It adds a migration but no new packages.
- Application uses an email-sender interface. Start with an offline fake/local inbox adapter;
  a production SMTP/API provider supplies delivery later. Email delivery follows challenge
  commit through an explicit post-commit boundary; a failed commit must not send a usable
  reset email. Do not put code/password content into RabbitMQ business messages.
- Delivery failure keeps the public acknowledgement uniform, records a sanitized internal
  failure and permits the user to retry under the same resend limits. Reliable delivery and
  failure tests belong to phase 3; no durable-delivery guarantee is claimed by this phase.
- Do not log/destructure the commands or email bodies: they contain addresses, codes and
  passwords. Existing request/pipeline logging records request names, not request values.
  Codes belong only in the recipient's email or isolated development inbox, never API JSON.

### Commit attempts, consumption and refresh revocation

`ResetPasswordOutcome` has `Completed` and `Rejected` cases. After incrementing a failed-code
counter or recording other attempt state, return
`Result<ResetPasswordOutcome>.Success(new ResetPasswordOutcome.Rejected())`. The pipeline
saves and commits before the endpoint maps it to `Error.InvalidPasswordReset()` (`422`).
Returning `Result.Failure` after incrementing would erase the counter and allow unbounded
guessing. Structural failures can roll back normally; atomic rate-limit increments must not
be undone by the later public rejection. No changes to shared transaction semantics are needed.

A valid reset must lock and re-read current user/challenge state, update the password through
the existing hasher and `User.ChangePassword`, consume/invalidate outstanding challenges,
and revoke all unrevoked refresh tokens using `RevokeAllForUserAsync` in one transaction.
Use the existing user-row session lock before challenge locks so refresh/reset operations
have a consistent lock order. Concurrent submissions of the same code allow one success.
Send a password-change notification after commit and require normal login afterward.

### Access-token policy

Retain the existing JWT expiry policy for this branch: successful reset revokes refresh
sessions and clears this browser's cookies, but already-issued access JWTs on other devices
can remain usable through `Auth:AccessTokenMinutes` (default 15 minutes) plus the current
30-second validator skew. This is not immediate global access-token invalidation. A per-user
authentication version/security stamp would require a separate schema/issuance/validation
change; it is deliberately not promised by the initial reset contract.

## Delivery checkpoints

1. Contracts, validation, errors and documented policy (implemented).
2. Challenge domain/persistence, code protection and adapter contracts (implemented).
3. Request/resend use case, atomic quotas and post-commit local email delivery (implemented).
4. Reset use case, committed rejection state, concurrency and refresh revocation.
5. HTTP/cookie integration, Swagger/local inbox guide and end-to-end acceptance.

## Phase 2 implementation boundaries

- `PasswordResetChallenge` receives timestamps and the attempt limit explicitly. It guards
  expiry, attempts, one-time consumption and idempotent invalidation without framework or
  clock dependencies. The caller allocates a version-7 id before computing the MAC.
- `PasswordResetCodeProtector` generates unbiased six-digit codes with the platform CSPRNG.
  HMAC-SHA256 covers a versioned purpose, challenge id, user id and code. Verification compares
  equal-length MAC bytes with the platform's
  [fixed-time comparison](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.cryptographicoperations.fixedtimeequals?view=net-10.0).
  The key is Base64 encoding at least 32 random bytes, supplied through secrets/environment.
- `EfPasswordResetChallengeStore` requires Read Committed and locks `identity.users` before
  reading or changing a user's challenges. Lookup returns refreshed, tracked user and latest
  challenge state, including expired/consumed/invalidated state for the future handler to reject.
  Dirty tracked state is rejected rather than silently overwritten.
- A partial unique index permits only one unconsumed, non-invalidated row per user. Expired
  or exhausted rows still occupy that slot until replacement explicitly invalidates them.
  Replacement invalidates existing rows with a transaction-scoped update before staging the
  insertion, avoiding dependence on EF's insertion/update ordering. The store never commits.
- `InvalidateOthersAsync` excludes the challenge being consumed so its pending consumption
  is preserved. Bulk changes reload clean tracked rows. All changes roll back with the caller's
  transaction. Never reuse the context's tracked state after a failed/rolled-back request.
- A CLR `uint Version` is mapped to PostgreSQL's existing
  [`xmin` system column](https://www.npgsql.org/efcore/modeling/concurrency.html) as an optimistic
  concurrency backstop. Database constraints enforce MAC shape, lifetimes, attempt bounds
  and mutually exclusive consumed/invalidated states. Foreign-key deletion cascades with the user.
- `IEmailSender` and `EmailMessage` describe only in-process post-commit delivery. No sender
  was registered in phase 2. Phase 3 supplies the local/offline adapters and explicit
  post-commit orchestration; request DTOs, raw codes and bodies are not broker messages.

## Phase 3 request and delivery behavior

- `ForgotPasswordHandler` obtains a quota decision before looking up the account. Client-wide
  refusal returns the existing rate-limit error; account-specific refusal returns the generic
  acknowledgement. Permitted unknown/inactive accounts perform equivalent code/MAC work but
  create no challenge or email. Only an eligible user receives a replacement challenge.
- After the locked lookup, time is sampled for issuance. The latest challenge's creation
  time provides a database cooldown backstop even if quota state was lost. A suppressed
  request preserves the existing challenge; a permitted replacement uses the phase 2 store.
- The forgot-password-specific outer behavior applies a 250 ms minimum to the whole command
  pipeline, including commit and local queue handoff. Common logging/validation/transaction
  ordering is unchanged. Padding reduces normal timing differences; network load and lock
  contention can exceed the floor, so exact constant response time is not promised.
- Redis uses one Lua operation and Redis server time for each decision. IP capacity is
  acquired first, including requests later suppressed per email. Email capacity is consumed
  only when its gate admits a request. Unknown/inactive accounts count identically; quota
  consumption is not refunded after database or delivery failure. Redis errors propagate
  without issuing a code (fail closed). Fixed windows and cooldown survive process restarts.
- The in-memory quota provider locks both decisions together and uses `TimeProvider` for
  deterministic offline tests. It is process-local and must not substitute for shared Redis
  quotas in a multi-instance deployment. Verification budgets are implemented for phase 4
  to call; no reset handler invokes them yet.
- Email identities use trimmed invariant case folding for quota keys. Parsed IPv4-mapped
  IPv6 addresses share their IPv4 bucket; missing/invalid IP uses one unknown-client bucket.
  Email/IP keys are purpose-separated HMACs under the protection secret. Redis keys never
  contain the raw identifiers. Rotating this key also changes quota identities.
- `IPostCommitEmailQueue` schedules a callback in the existing scoped commit buffer. The
  callback hands the email to a bounded process-local channel only after commit. Rollback,
  save failure and commit failure discard the callback. No code content reaches RabbitMQ.
- A background worker sends queued emails independently of the HTTP response, skips expired
  code emails, and logs sanitized delivery failure types without addresses, body content or
  provider exception text. It continues after failures. Queue saturation is logged and drops
  the email while preserving the generic acknowledgement. This operation only acknowledges
  the request and does not claim a password was changed.
- This is best-effort delivery, not an outbox. A process crash can lose queued email. The
  user can request another code after cooldown, subject to the same quotas. Provider network
  latency and failures do not expose account eligibility through the request response.
- `Email:Provider=InMemory` is the safe offline default and keeps a bounded inbox accessible
  through DI only. `Email:Provider=Mailpit` uses its
  [HTTP send API](https://mailpit.axllent.org/docs/api-v1/) at `Email:MailpitBaseUrl`
  (default `http://localhost:8025/`). This local adapter permits loopback HTTP(S) endpoints,
  disallows URL credentials/query/fragment, and does not follow redirects. It expects an
  unauthenticated local Mailpit inbox; a production SMTP/API adapter is a separate choice.
- Email options also define sender name/address, a 5-second HTTP timeout and capacity 256.
  Options validate on host start. Code protection and quota options still validate on
  resolution until recovery HTTP startup validation is enabled in phase 5. No new packages,
  migrations, production configuration or review-only Markdown files are added in phase 3.

### Phase 3 verification

On 2026-11-22, the Release build passed with zero warnings/errors. The unit, architecture and
integration projects passed 202, 11 and 168 tests respectively (381 total, zero failed/skipped).
Integration tests use disposable PostgreSQL/Redis/RabbitMQ and Mailpit v1.27.0. They verify
commit-before-delivery, rollback without email, replacement, real Mailpit capture and shared
atomic Redis budgets. The shared API fixture pins offline email configuration. No developer
database or secrets are used or modified by these checks.

## Consequences

The existing auth endpoints continue to behave as before. Registration now shares its
password validator with recovery, including safe handling of a missing password. Domain,
code-protection and storage components can be reviewed independently of the parent branch's
session implementation. Phase 1 tests check credential boundaries and commit semantics;
phase 2 adds lifecycle, keyed protection, configuration, migration/model and PostgreSQL
locking/concurrency tests. Phase 3 adds the request/resend use case, request timing, atomic
quotas, transactional email scheduling and local delivery tests. Reset execution, recovery
HTTP/cookie integration and production email delivery remain later work.

Reference: [OWASP Forgot Password Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Forgot_Password_Cheat_Sheet.html).
