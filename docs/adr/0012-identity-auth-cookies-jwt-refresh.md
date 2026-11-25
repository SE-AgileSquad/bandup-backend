# 0012 — Identity: cookie-carried JWT, opaque refresh tokens, adapter-backed CAPTCHA

**Status:** Accepted.

**Follow-up:** [ADR 0016](0016-refresh-logout-contracts-and-commit-outcomes.md) defines the
refresh/logout HTTP contracts, committed reuse-revocation outcome and concurrency requirements.

## Context

Tier 1 needs register, login, logout, refresh and role checks (AUTH-01 to AUTH-11). The product
specification fixes most of the decisions: JWT access tokens carried in **HttpOnly cookies**
(`bu_at`), opaque rotating refresh tokens (`bu_rt`), CAPTCHA on register **and** login, and a
fallback for CAPTCHA outages. What the spec leaves open is **how those pieces fit this
codebase**: where token validation runs, how revocation works with the foundations we already
have, and what the CAPTCHA adapter looks like.

## Decision

- **Access token:** JWT, HS256, 15 minutes, claims `sub`, `role`, `jti`. Carried in cookie
  `bu_at` (`HttpOnly; Secure; SameSite=Strict`). Validated by the standard `JwtBearer` handler,
  customised twice: the token is read from the cookie rather than an `Authorization` header
  (`OnMessageReceived`), and each validated token's `jti` is checked against the Redis blacklist
  (`OnTokenValidated`) — the same `ICacheService` the foundation already provides.
- **Refresh token:** 256-bit opaque random value. The client holds the raw token in cookie
  `bu_rt`; the database stores only its **SHA-256 hash** with `expires_at`, `revoked_at` and
  `replaced_by_id`. Refresh **rotates**: the old row is revoked, a new one inserted. **Reuse of a
  revoked token means theft — all active refresh tokens of that user are revoked.**
- **Logout:** revoke the refresh row and add the access token's `jti` to the Redis blacklist
  with the token's remaining lifetime as the TTL. A 15-minute blacklist is cheap; a token table
  would not be.
- **Passwords:** BCrypt (work factor ≥ 12) behind `IPasswordHasher`. Login with an unknown email
  verifies against a **dummy hash** so both failure paths take similar time (AUTH-07).
- **CAPTCHA:** `ICaptchaVerifier` with three outcomes — valid, invalid token, provider
  unavailable. Real implementation (Turnstile) and `FakeCaptchaVerifier` selected by
  `Captcha__Provider`, like every other external dependency. An `OutageTolerantCaptchaVerifier`
  decorator implements AUTH-05: on unavailability, allow if the client IP has fewer than three
  attempts in the last hour (Redis counter), else `422 CAPTCHA_UNAVAILABLE`.
- **CSRF:** state-changing endpoints require `Content-Type: application/json` and the header
  `X-Requested-With: bandup`, checked by middleware. `SameSite=Strict` is the primary defence;
  the header is the second.
- **`ICurrentUser`** (`UserId`, `Role`, `IsAuthenticated`) lives in Application; the Api
  implementation reads `HttpContext.User`, the Worker gets a null implementation. The `userId`
  log property is enriched from it.
- **Module schema `identity`** holds `users` and `refresh_tokens`. This is the first module
  schema, and its migration sets the precedent in
  [ADR 0015](0015-module-schemas-and-sequential-migrations.md).

## Consequences

- Authentication reuses four existing foundations — `ICacheService` (blacklist and fallback
  counter), `Result`/`ErrorType` (the spec's auth error codes already have enum members), the
  transaction behavior (register + credit account atomically), and the adapter pattern
  (CAPTCHA, hashing, tokens are all swappable fakes).
- The `jti` blacklist costs one Redis key per active access token at any moment, bounded by the
  15-minute TTL. Losing Redis widens logout's enforcement window to 15 minutes rather than
  disabling authentication.
- `Secure` cookies require HTTPS. Development runs on plain HTTP, so the cookie policy must not
  hard-fail without TLS — configured, not asserted.
- Cost: three security-critical components (token service, blacklist, rotation) are hand-built.
  The trade is identical to [ADR 0002](0002-cqrs-lite-with-in-house-dispatcher.md): the spec's
  constraints are specific enough that a generic library would be configured to the same shape
  with less testability.
- Rejected: refresh tokens as JWTs (revocation would still need a blacklist, so the simpler
  opaque token wins). Rejected: per-request database lookups for role checks (the role claim is
  fresh enough for a 15-minute window; demotion latency is an accepted bound). Rejected:
  walking `replaced_by_id` to revoke only the stolen chain — revoking all of the user's tokens
  is simpler and fails safe.
