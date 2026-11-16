# 0016 — Refresh/logout contracts and committed revocation outcomes

**Status:** Accepted. Phases 1–3 define the contract, add the supporting adapters and implement
the refresh command/handler. Logout, HTTP endpoints, cookie changes and JWT blacklist
enforcement follow in later phases.

## Context

[ADR 0012](0012-identity-auth-cookies-jwt-refresh.md) defines rotating refresh tokens and
logout. AUTH-09 requires a reused token to be rejected while revoking the user's refresh
sessions. The existing transaction behavior rolls back every failed `Result`, so returning
`Result.Failure(Error.InvalidRefreshToken())` after changing those rows would undo revocation.

The HTTP contract, cookie lifecycle and concurrency rules must be explicit before implementing
the handlers. Domain and Application remain independent of HTTP, EF Core and Redis types.

## Decision

### HTTP and cookie contracts

| Endpoint | Credential | Success | Expected rejection |
|---|---|---|---|
| `POST /api/auth/refresh` | `bu_rt` cookie; no valid access token required | `200`, empty body, replacement `bu_at` and `bu_rt` cookies | `401 INVALID_REFRESH_TOKEN` for a missing, unknown, expired or revoked token, or a missing/inactive user |
| `POST /api/auth/logout` | Valid authenticated access token in `bu_at`; `bu_rt` identifies the refresh token to revoke | `204`, empty body, both cookies deleted | `401` when access authentication fails |

- Neither request accepts tokens in a JSON body, and neither response serializes tokens.
  Clients send `{}` with `Content-Type: application/json` and `X-Requested-With: bandup` to
  satisfy the existing CSRF middleware. Requests carrying either session cookie and failing
  that guard receive `400 VALIDATION_ERROR` before a handler runs. Authentication failures
  remain subject to the host's authentication/authorization middleware ordering.
- Refresh is not protected by access-token authorization: an expired access token is the
  normal reason to call it. Missing/invalid refresh credentials use the auth error above,
  rather than a FluentValidation `400`.
- Refresh `401` responses clear both cookies. On successful logout, the API clears both
  cookies after the command completes. Deletion uses the same path/domain as issuance.
  Both cookies use an explicit `Path=/`, `HttpOnly`, `SameSite=Strict` and the configured
  `Secure` policy. Cookie expiry matches the associated issued token's expiry.
- The API reads cookies, maps outcomes to HTTP and writes/deletes cookies. Application handlers
  implement session rules. Raw refresh tokens and issued outcomes must never be logged.
- Access and refresh lifetimes use `Auth:AccessTokenMinutes` and `Auth:RefreshTokenDays`.
  Login and refresh use the same issuance policy through an Application-facing abstraction;
  Application must not reference Infrastructure's `AuthSettings`.

### Commit the security action before rejecting refresh

Introduce `RefreshOutcome` in Application with two distinct cases:

- `Issued`: an access token, raw refresh token and refresh-token expiry for cookie issuance.
- `RejectedAfterRevocation`: reuse was detected and all of that user's unrevoked refresh
  tokens were revoked. This case cannot carry replacement tokens.

The handler returns `Result<RefreshOutcome>.Success(new
RefreshOutcome.RejectedAfterRevocation())` only after recording the required revocations in
the current transaction. The existing pipeline saves and commits them. After dispatch
completes, the endpoint maps this case through `Error.InvalidRefreshToken()` to the same
ProblemDetails `401` as any other invalid refresh. A client cannot distinguish reuse from
other invalid credentials by the public error code or message.

For rejections that need no committed changes, return
`Result<RefreshOutcome>.Failure(Error.InvalidRefreshToken())`. They roll back normally.
Exceptions, save failures and commit failures propagate without issuing replacement cookies
or claiming successful revocation. No special commit-on-failure flag, nested commit or change
to the shared `Result`/transaction behavior is introduced.

This is a narrow refinement of [ADR 0003](0003-result-pattern.md): the successful command
result means the security action completed, not that a session was issued. The handler must
log a structured warning for detected reuse because the generic pipeline logs a completed
command as successful. It must not log token values or hashes.

### Rotation, reuse and concurrency

- Only hashes are persisted. A successful rotation revokes the old token, sets its
  `replaced_by_id`, inserts the successor and commits before cookies are written.
- A known revoked token triggers reuse handling, including when it is also expired. Revoked
  rows remain available for reuse detection. Reuse revokes all unrevoked refresh tokens of
  that user, following ADR 0012; it does not revoke every outstanding access JWT.
- Rotation and reuse handling must serialize changes to the affected user's refresh sessions
  within the database transaction. Infrastructure must lock and re-read state or provide an
  equivalent atomic operation; an ordinary tracked read followed by `SaveChanges` is not a
  concurrency guarantee. Coordinate logout revocation through the same mechanism. Preserve a
  consistent lock order and test with separate connections.
- Two requests cannot both rotate the same token. Under strict reuse detection the losing
  request revokes the successor issued by the winner. The frontend must coordinate refresh
  requests rather than refresh in parallel. This is documented behavior, not a grace window.
- New refresh tokens are issued only for an existing, active user, using the user's current
  role. Unknown or expired unrevoked credentials never issue a replacement session.

### Logout and access-token revocation

- Obtain user id, `jti` and expiry from the validated authentication context through an
  Application interface. Do not trust decoded, unvalidated JWT claims or request-body ids.
- Revoke the presented refresh token only if it belongs to the authenticated user. A missing,
  unknown or foreign refresh cookie does not revoke another user's session; it does not
  prevent blacklisting the caller's access token and clearing this browser's cookies.
- A token already revoked needs no second mutation. Repeated HTTP logout may return `401`
  because the access token has already been blacklisted; the endpoint does not promise an
  anonymous or expired-access-token logout path.
- Store one blacklist entry per `jti`. Its TTL covers the remaining JWT validation window,
  including the validator's current 30-second clock skew. Reject known blacklisted tokens
  during token validation, so logout cannot be bypassed by restoring a copied access cookie.
- Follow ADR 0012's availability choice: a Redis read outage logs a warning and permits
  otherwise-valid JWT authentication until the validation window ends. A logout blacklist
  write failure propagates as an infrastructure failure; do not return `204` or clear cookies
  as though server-side logout succeeded. PostgreSQL and Redis have no shared transaction,
  so partial-failure behavior must be tested and documented with the adapter implementation.

## Verification and consequences

- Phase 1 adds a test-only command using the real dispatcher, transaction pipeline and
  PostgreSQL token rows. It proves that `RejectedAfterRevocation` commits revocations before
  dispatch returns, while an ordinary failed refresh result rolls its mutations back. This
  verifies the transaction contract, not the future refresh/reuse handler or HTTP mapping.
- Later phases must verify atomic rotation, concurrent refresh/reuse and logout races,
  inactive users, cookie deletion/attributes, CSRF, blacklist expiry and Redis failures through
  integration tests before AUTH-09/AUTH-10 are considered implemented.
- Phase 2 reuses the existing schema with the locking, revocation and issuance adapters
  described below. It requires no migration or new package.
- The two refresh outcomes carry different data so a revocation rejection cannot accidentally
  supply replacement cookies. All callers must handle both cases explicitly after dispatch.

## Phase 2 implementation

- `IRefreshTokenStore.FindSessionForUpdateAsync` first locates the owner by token hash, locks
  that user's row with PostgreSQL `SELECT ... FOR UPDATE`, then re-reads the token and user.
  The returned user is a fresh snapshot; the token is tracked for mutation. An existing tracked
  token is refreshed without overwriting pending changes: callers must lock before mutating.
  Unknown credentials return null; revoked and expired rows are still returned for classification.
- Session insertion, single-token revocation and all-user revocation acquire the same user-row
  lock, held until the existing transaction ends. They require the unit of work's default
  Read Committed isolation; a repeatable-read snapshot could hide preceding token changes even
  after the lock wait. Missing or unsupported transactions are rejected. Saving/committing is
  left to the caller. No token lock precedes the user lock. All-user revocation
  includes expired unrevoked rows; single-token revocation filters by the authenticated user
  before touching a token, so a foreign cookie never causes a foreign-user lock or mutation.
- `ISessionTokenIssuer` creates the access JWT, opaque refresh value and hashed refresh row
  using the configured lifetimes. Login now uses this policy and returns internal refresh
  expiry metadata for the later cookie phase. Its public JSON response is unchanged. Refresh
  uses the same policy with the fresh user's current role.
- `IAccessTokenRevocationStore` uses the existing cache abstraction, backed by Redis or its
  in-memory fake. Keys are `auth:revoked-jti:<id>` under the cache's `bandup:` prefix. Retention
  ends at JWT expiry plus the shared 30-second validation skew. Redis read failures warn and
  fail open; write failures propagate. Cancellation and unexpected errors propagate too.
  The store is registered now; authentication enforcement and logout calls follow later.
- Revocation-store writes are immediate and independent of the PostgreSQL transaction. A
  successful Redis write followed by a database rollback leaves the access token blacklisted;
  a Redis write failure can roll back database changes through the command pipeline. The
  logout phase must test write and commit failures and must never claim atomicity across stores.
- Adapter integration tests use separate PostgreSQL connections and observe an actual lock
  wait before releasing the first transaction. They cover committed rotation, rollback, stale
  tracked state, current user role, concurrent insertion/revocation, owner isolation and Redis
  TTL. These are adapter probes, not proof of the future AUTH-09/AUTH-10 HTTP flows.

## Phase 3 implementation

- `RefreshCommand` carries the nullable raw refresh-cookie value and optional user agent.
  `RefreshHandler` is discovered by the existing Application handler registration. It needs
  no access-token identity or CAPTCHA. No validator turns missing credentials into a `400`;
  missing/blank, unknown, expired unrevoked and inactive-user credentials return the existing
  `INVALID_REFRESH_TOKEN` failure. A missing user is returned as an unknown session by the store.
- The handler hashes the presented credential and uses `FindSessionForUpdateAsync` before
  classifying it. It reads `TimeProvider` after acquiring the lock, so credentials that expire
  while waiting are rejected. Revocation is checked before expiry or account activity: a known
  revoked token always triggers all-user refresh revocation, including for an inactive user.
- Reuse logs a structured warning containing the user id, without token values or hashes,
  and returns `RejectedAfterRevocation` as a completed result. The existing pipeline commits
  the revocations before dispatch returns. Storage/save/commit failures propagate instead.
- For an active user with an unrevoked, unexpired token, the shared issuer creates a successor
  with the current role and configured lifetimes. `Rotate` revokes the old row and sets its
  replacement id; insertion joins the same transaction. Only after commit does dispatch return
  the issued access token, raw refresh token and refresh expiry for the later cookie endpoint.
- Handler-level integration tests verify committed rotation and reuse, failed-result behavior,
  rollback after flushed mutations and simulated commit failures. Concurrent real refresh
  commands serialize: the loser detects reuse and revokes the winner's successor, as specified
  above. HTTP status mapping, cookie behavior and complete AUTH-09/AUTH-10 acceptance remain
  for the endpoint/logout phases.
