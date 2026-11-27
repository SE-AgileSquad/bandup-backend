# ADR 0019: Separate password-reset code verification

## Decision

Recovery uses three anonymous JSON endpoints:

| Endpoint | Body | Success |
|---|---|---|
| `POST /api/auth/forgot-password` | `email` | Generic `202`; email delivery is best-effort |
| `POST /api/auth/verify-reset-code` | `email`, six-digit `code` string | `200 { resetToken, expiresAt }`; no cookie or password changes |
| `POST /api/auth/reset-password` | `resetToken`, `newPassword` | Empty `204`; clear this browser's session cookies after commit |

This supersedes the combined code/password request in ADR 0017. The former reset body is
rejected with `400`; frontend callers must verify the code first and use the returned token.
OTP sign-in remains separate: its verification route issues login cookies, not reset tokens.

## Authorization and lifecycle

Successful code verification consumes the email challenge and issues one reset-only token
in the same transaction. Incorrect guesses return a committable rejected outcome, preserving
the existing five-guess limit. Invalid/expired/closed codes share `422 INVALID_PASSWORD_RESET_CODE`.
The ten-minute code lifetime and sixty-second resend cooldown are unchanged.

The authorization is a cryptographically random 256-bit opaque value encoded as 43 base64url
characters. Only its keyed MAC is stored in `identity.password_reset_tokens`. The existing
`PasswordReset:CodeProtectionKey` is reused with a distinct `bandup:password-reset:authorization:v1`
purpose; no new secret or external provider is required. Key rotation invalidates both
outstanding codes and authorizations. Tokens are bound to their challenge and user, expire
five minutes after verification, and can authorize one successful reset. Database uniqueness
allows one authorization per challenge and one open authorization per user.

Admitted resends invalidate prior authorizations in the same transaction as replacement.
Suppressed resends preserve them. Reset lookup also checks the latest challenge relationship,
so direct challenge replacement cannot leave an older authorization usable.

Reset rechecks active status and token lifetime under the shared user-row lock, including
after password hashing. Token consumption, password change, other recovery-artifact
invalidation, refresh-session revocation and pending OTP-login-code invalidation commit
together. Notification email is queued only after commit. Save/commit failure leaves the
authorization retryable and does not return success cookies or a verification credential.

Concurrent verification issues one authorization; concurrent reset consumes it once. User-row
locking precedes fresh token reads; `xmin` protects contested rows. Already-issued access JWTs
on other devices retain normal expiry. Verification does not sign in, grant credits, revoke
sessions or send a notification.

## Transport and quotas

Verification responses use `Cache-Control: no-store` and `Pragma: no-cache`. Frontends should
hold the token in memory, never URLs or persistent browser storage, and discard it after reset,
expiry or restart of recovery. Passwords, codes and tokens must not be logged. Existing
JSON/`X-Requested-With: bandup` CSRF protection applies whenever session cookies accompany
any recovery request. Neither the access-cookie nor refresh endpoint accepts this token.

Code-verification quotas remain twenty per email and one hundred per IP per fifteen-minute
window. Final-reset quotas use independent `reset` buckets: twenty attempts per protected
token and one hundred per IP per fifteen-minute window. Redis enforces atomic shared quotas;
the in-memory adapter is for offline single-process tests. Opaque identifiers use a dedicated
quota purpose. IP suppression returns `429 PASSWORD_RESET_RATE_LIMITED`; token suppression,
unknown, expired, invalidated and consumed tokens share `422 INVALID_PASSWORD_RESET_TOKEN`.
Malformed JSON, code/token formats and password-policy failures use `400` without consuming
valid credentials. Quota/database failures fail closed. Reset errors preserve browser cookies.

## Integration

Apply `AddPasswordResetTokens` before using this contract. Retain existing JWT, recovery,
OTP-login and email configuration. The existing Mailpit/Resend adapters deliver the same code
and password-change emails; no new SDK or package is introduced. Worker configuration is unchanged.
Email delivery remains in-process and best-effort without automatic retries.
