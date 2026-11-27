# ADR 0018: Email OTP sign-in contracts and policy

## Status

Implemented end to end: Application contracts/handlers, challenge persistence, code protection,
Redis/in-memory quotas, post-commit email, HTTP endpoints and normal session cookies. Password
reset now invalidates outstanding login challenges in its existing transaction.

## Context

AUTH-16 adds email-and-code sign-in as an alternative to password sign-in for an existing,
active account. It is a passwordless login method, not a second factor. It does not register
accounts, change passwords, open credit accounts or grant signup credits.

Reuse the existing session issuer, refresh-token store, cookie service and post-commit email
queue. The Resend adapter only delivers the email; BandUp generates and verifies the code.
Recovery challenges must never be used as login challenges.

## Decision

### HTTP contract

| Route | Client JSON | Success | Expected rejection |
|---|---|---|---|
| `POST /api/auth/otp/request` | `{ "email": "learner@example.com", "captchaToken": "fake-captcha-token" }` | `202` with the generic `RequestOtpLoginOutcome` message; no cookies | `400 VALIDATION_ERROR`; existing CAPTCHA errors; IP-wide `429 OTP_LOGIN_RATE_LIMITED` |
| `POST /api/auth/otp/verify` | `{ "email": "learner@example.com", "code": "012345" }` | `200` with the existing public login user shape and replacement access/refresh cookies after commit | `400 VALIDATION_ERROR`; generic `401 INVALID_OTP_LOGIN_CODE`; IP-wide `429 OTP_LOGIN_RATE_LIMITED`; no cookie changes |

Both routes allow anonymous callers. The existing JSON/`X-Requested-With: bandup` CSRF
guard still applies whenever either session cookie is present. Requesting a code never signs
the caller in. Verification requires no password or CAPTCHA token.

The request acknowledgement is identical for an eligible account, an unknown/inactive
account, cooldown, per-email quota suppression and best-effort delivery failure. It does not
contain challenge IDs, codes, account-specific retry information or remaining attempts.
CAPTCHA validation and IP gates apply without revealing account eligibility.

Verification uses one public rejection for unknown/inactive accounts, absent challenges,
wrong/expired/consumed/superseded codes and exhausted per-email verification quotas. Clients
must display this `401` on the sign-in form, not trigger session refresh or retry the code.
Only a protected resource's session `401` should enter the existing refresh-and-retry flow.

HTTP request records expose only the listed JSON fields. `ClientIp` on both commands and
`UserAgent` on verification are populated by the API. Obtain the IP from the connection under
the existing trusted-forwarding policy, never a JSON field or an independently trusted
forwarding header. Missing IP uses a shared bounded bucket. User-Agent is session metadata,
not proof of identity.

### Validation and policy

Email uses the registration email validation and 320-character limit. Request CAPTCHA tokens
must be nonempty and at most 2,048 characters; provider verification belongs in the handler.
Codes are strings of exactly six ASCII digits. Preserve leading zeroes; reject whitespace,
non-ASCII digits and extra characters rather than trimming or normalizing credentials.
Validators do not perform account, CAPTCHA-provider or challenge lookups.

`OtpLoginPolicy` owns these limits independently of `PasswordResetPolicy`:

| Rule | Value |
|---|---|
| Code lifetime | 10 minutes |
| Incorrect guesses per challenge | 5; the fifth exhausts the challenge |
| Resend cooldown | 60 seconds |
| Accepted requests per email | 5 per hour |
| Request attempts admitted by the IP gate | 20 per hour, including later email-specific suppression |
| Verification attempts per email | 20 per 15 minutes, across replacements |
| Verification attempts per IP | 100 per 15 minutes |
| Minimum request/verification duration | 250 ms, plus comparable work for ineligible accounts |

The Redis adapter uses atomic counters, fixed UTC windows and a dedicated login
namespace. Use opaque keyed hashes of email/IP, with email identity matching case-insensitive
account lookup. A resend does not reset window budgets. Account suppression affects OTP login
only and does not deactivate the user or block password login or password recovery.

An admitted replacement supersedes the previous login challenge. Suppression must not
invalidate an otherwise usable challenge. A code is usable only while `now < ExpiresAt`,
before the guess limit and while unused and not invalidated.

### Protection and delivery

`OtpLoginChallenge` has its own `identity.otp_login_challenges` PostgreSQL table. Protect codes
with a user- and challenge-bound keyed MAC using `OtpLoginPolicy.ProtectionPurpose`
(`bandup:otp-login:v1`). The existing reset protector is bound to `bandup:password-reset:v1`
and cannot be reused unchanged. Do not persist plaintext codes or use an unkeyed hash of the
small six-digit search space. Generate codes cryptographically and compare MACs in constant
time.

Use a separate `OtpLogin:CodeProtectionKey`, Base64 encoding at least 32 random bytes.
Provider selection uses `OtpLogin:RateLimitProvider` with Redis and an in-memory test adapter.
The API validates both settings on startup; the Worker does not require this key. Generate
the new key once without rotating existing JWT or recovery keys. CAPTCHA is checked before
reserving an email cooldown so an invalid CAPTCHA cannot consume that email's send slot.

Queue email through the existing post-commit boundary; rollback must not deliver a usable
code. Delivery is best-effort and in-process, with no outbox or automatic retries. A `202`
is not a delivery receipt. Never return or log raw codes, commands, email bodies or session
credentials, and never put code content in RabbitMQ messages.

### Transactional verification

Use the existing user-row session lock shared with refresh and recovery. Re-read account
eligibility and challenge state under the lock. No separate challenge-row lock precedes it.
Consume a correct challenge, record `User.RecordLogin` and add the issued
refresh token in one transaction. Concurrent verification permits one successful consumption.
Expose the session cookies only after commit.

`VerifyOtpLoginOutcome.Completed` carries the existing internal `LoginResult` for the endpoint
to issue cookies and map public user JSON. Never serialize this outcome directly. This
feature preserves the existing login response's credit placeholder; `/me` remains
the authoritative balance read.

When a wrong guess changes persistent challenge state, return a successful `Result` carrying
`VerifyOtpLoginOutcome.Rejected`. The transaction commits before the endpoint maps that
outcome to `Error.InvalidOtpLogin()` (`401`). Returning `Result.Failure` after incrementing a
counter would roll the counter back. Structural failures may roll back normally; Redis
quota increments are independent of the database transaction.

Successful password reset must invalidate outstanding login challenges in the same database
transaction as password replacement and refresh revocation. Check active status at request
and verification. Explicit challenge invalidation on deactivation/reactivation belongs to
the account lifecycle workflow when that workflow is exposed; this feature does not add
a deactivation/reactivation endpoint.

## Consequences

- Application scanning discovers handlers/validators. Domain/Application have no dependency
  on EF, Redis, HTTP or the email provider. Infrastructure implements the new interfaces.
- The migration adds only login challenges, constraints and indexes. One open challenge per
  user is enforced by a partial unique index; `xmin` guards stale writes. Replacements bulk
  invalidate the old row before staging the new one, inside the same transaction.
- HTTP/database tests cover expiry, replacement, purpose separation, committed guesses,
  concurrent verification, rollback and reset/verification races with disposable dependencies.
- Existing password login, CAPTCHA errors, cookie transport, refresh and logout remain the
  session foundation. Email login is an additional entry point, not a separate session type.
- The Resend test-sender recipient restriction still applies; offline tests must not contact
  a real inbox or use developer credentials.
