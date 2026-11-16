# 0016 — Refresh/logout contracts and committed revocation outcomes

**Status:** Accepted. Phase 1 defines the contract and verifies transaction semantics;
handlers, endpoints, cookie changes and Infrastructure adapters follow in later phases.

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

The future handler returns `Result<RefreshOutcome>.Success(new
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
- The existing schema can be reused if database locking provides the concurrency boundary.
  Repository locking, revocation adapters, session issuance policy, DI wiring and any required
  migration belong to the next phase. No new package is required for this decision.
- The two refresh outcomes carry different data so a revocation rejection cannot accidentally
  supply replacement cookies. All callers must handle both cases explicitly after dispatch.
