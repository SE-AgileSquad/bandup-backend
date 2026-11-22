# 0017 — Email password-reset contracts and policy

**Status:** Phases 1–5 implemented: contracts/validation, challenge storage/protection,
request/resend, atomic quotas, post-commit local email delivery, atomic password-reset
execution and local HTTP/cookie/Swagger integration. Phase 6 connects the selected real
email service. Password recovery is available in Swagger with a local Mailpit inbox.

## Context

AUTH-15 extends Identity with email codes for forgotten-password recovery. Registration,
password hashing, `User.ChangePassword`, refresh-session revocation and the transaction
pipeline already exist. This feature uses them without issuing a login session after reset.

As with [refresh reuse](0016-refresh-logout-contracts-and-commit-outcomes.md), an ordinary
failed `Result` rolls back database writes. Incorrect-code counters must survive rejection.
Domain and Application must remain independent of HTTP, EF Core and email-provider SDKs.

## Decision

### HTTP contract

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
  protection secret or selected real email-provider credentials are missing. Phase 2 added
  `PasswordReset:CodeProtectionKey` and validation when code protection is resolved. Phase 5
  also enables API startup validation now that recovery is exposed. The Worker does not need
  the recovery key. Phase 2 added a migration but no new packages.
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
4. Reset use case, committed rejection state, concurrency and refresh revocation (implemented).
5. Local HTTP/cookie integration, Swagger/local inbox setup and end-to-end acceptance (implemented).
6. Real email-provider adapter/configuration and real-inbox acceptance, after provider setup.

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
  challenge state, including expired/consumed/invalidated state for the handler to reject.
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
  quotas in a multi-instance deployment. Phase 4's reset handler now consumes the verification
  budgets before reading account/challenge state.
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

## Phase 4 reset execution

- `ResetPasswordHandler` checks the shared verification quotas before looking up an account.
  Client-IP refusal uses the rate-limit error; account-specific refusal and unknown/inactive,
  absent, expired, consumed, invalidated or exhausted challenges use the same `Rejected` outcome.
  No password hashing, session revocation or notification occurs on those rejections.
- The handler locks and reads current state through the existing challenge store, then samples
  time and checks usability. A wrong MAC increments the challenge's failed-attempt counter
  only while still usable. Returning a successful `Result` containing `Rejected` lets the
  pipeline commit that counter; the HTTP endpoint then maps it to generic `422`.
- On a correct code, the existing password hasher computes the replacement hash. Cancellation
  and expiry are checked again before staging writes because hashing can outlast validity.
  The handler then changes the password, consumes the challenge, invalidates other outstanding
  challenges while retaining the consumed id, and revokes all owned unrevoked refresh rows,
  including expired rows, in the same command transaction.
- The pipeline saves/commits all changes together. Save/commit failure rolls back password,
  consumption, counters and refresh revocation and discards the notification callback. An
  infrastructure failure never returns `Completed` or claims that reset succeeded.
- The password-change notification is scheduled through the existing post-commit queue and
  contains neither the new password nor the OTP. No session is issued automatically. Users
  sign in normally with the new password after reset.
- `ResetPasswordTimingBehavior` applies the same 250 ms minimum around the reset pipeline,
  including rejection-counter commits. It reduces normal account-state timing differences
  without promising exact constant duration under load. Shared transaction behavior and
  existing login/refresh/logout implementations are unchanged.
- The user-row lock serializes concurrent use of one code: one request can consume it; the
  next sees the committed consumed state. It also coordinates with refresh: tokens rotated
  before reset are revoked by reset, and a refresh after reset sees the revoked credential.
  Already-issued access JWTs retain the existing expiry policy described above.
- This checkpoint adds Application handlers/behavior and tests, using the existing Domain
  and Infrastructure contracts. No new schema, package, secret or email-provider account is
  required. HTTP responses/cookie clearing remain phase 5; real provider delivery is phase 6.

### Phase 4 verification

On 2026-11-22, the Release build passed with zero warnings/errors. The unit, architecture and
integration projects passed 222, 11 and 181 tests respectively (414 total, zero failed/skipped).
Tests cover committed wrong-attempt counters, expiry after locking and hashing, generic
ineligible-state rejection, password/code/session rollback, concurrent code consumption,
reset/refresh ordering and post-commit notifications. The successful flow verifies that the
old password stops working and the new password can sign in. These checks use disposable
dependencies and offline email configuration; no real provider or developer database is used.

## Phase 5 HTTP and local acceptance

- The Auth endpoint group exposes anonymous JSON `forgot-password` and `reset-password`
  routes using thin request DTOs. They dispatch the existing commands and derive client IP
  from the connection under the host's forwarded-header policy. Body-supplied IP and
  untrusted forwarding headers do not replace that value.
- Forgot-password returns `202` with only the generic acknowledgement and no Location,
  code, challenge id, cookie mutation or account-specific retry hint. Reset maps the
  committed `Rejected` outcome to generic `422`; it deletes both configured session cookies
  only for `Completed`, after dispatch/commit, and returns empty `204` without a new session.
- The existing CSRF guard remains active when either session cookie accompanies recovery.
  Stale/malformed access cookies do not impose authentication on these anonymous routes.
  Validation, throttling and infrastructure failures preserve cookies. Shared ProblemDetails
  include the stable error code and request-id reference; the router's unsupported-media-type
  response is empty `415`. OpenAPI documents the request DTOs,
  anonymous contract, success body and `400`/`415`/`422`/`429`/`500` responses as applicable.
- API startup now validates `PasswordResetSettings`, including the independent Base64 key
  and selected quota provider. The Infrastructure binding remains lazy for other hosts:
  the Worker starts without the recovery key. The API's checked-in settings contain only
  empty secret placeholders and safe offline email defaults.
- The optional `recovery` Compose profile starts Mailpit v1.27.0 on loopback HTTP port 8025,
  configurable with `MAILPIT_HTTP_PORT`. SMTP is not published. The local setup guide covers
  key generation, Mailpit selection, Swagger request/reset/login and delivery limitations.
  The browser contract includes reset coordination with pending refreshes across tabs.
- Acceptance checks cover cookie jars over HTTP and HTTPS policy, custom names, old/new
  login, copied refresh rejection, retained access-token expiry, post-commit notification,
  uniform account-state responses, committed wrong attempts, CSRF, invalid JSON/credentials,
  quotas, untrusted IP input, rollback, sanitized logs, startup and OpenAPI. A real local
  Mailpit/Redis/PostgreSQL flow also exercises the public recovery endpoints.
- No schema, package or real-provider credential is added. Local email remains asynchronous
  and best-effort; real email-provider integration and real-inbox acceptance remain phase 6.

### Phase 5 verification

On 2026-11-22, the Release build passed with zero warnings/errors. The unit, architecture and
integration projects passed 222, 11 and 231 tests respectively (464 total, zero failed/skipped).
The 50 new HTTP cases exercise the public recovery flow and boundaries listed above. Actual
local Mailpit delivery is tested together with Redis quotas and PostgreSQL password changes;
the existing Swagger asset tests also remain green. The optional `recovery` Compose profile
validates using the example environment file. Unsupported content types retain the router's
empty `415` response. No developer secrets/database or real email service is changed. Native
browser interaction and production email delivery are not part of these automated checks.

## Consequences

The existing auth endpoints continue to behave as before. Registration now shares its
password validator with recovery, including safe handling of a missing password. Domain,
code-protection and storage components can be reviewed independently of the parent branch's
session implementation. Phase 1 tests check credential boundaries and commit semantics;
phase 2 adds lifecycle, keyed protection, configuration, migration/model and PostgreSQL
locking/concurrency tests. Phase 3 adds the request/resend use case, request timing, atomic
quotas, transactional email scheduling and local delivery tests. Phase 4 adds atomic reset,
committed rejection counters, notifications and reset/refresh concurrency checks. Recovery
HTTP/cookie integration is implemented in phase 5. Real-provider email delivery remains phase 6.

Reference: [OWASP Forgot Password Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Forgot_Password_Cheat_Sheet.html).
