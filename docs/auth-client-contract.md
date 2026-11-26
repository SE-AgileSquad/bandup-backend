# Browser session contract

The API owns `bu_at` and `bu_rt` as host-only `HttpOnly` cookies with `Path=/` and
`SameSite=Strict`. `Auth:RequireSecureCookies` controls `Secure`. Login and refresh set cookie
expiry to the issued token expiry; successful logout, successful password reset and rejected
refresh delete both cookies.
Names can be configured, so clients should treat the cookies as browser-managed credentials.

## Requests and responses

| Route | Credentials | Success | Expected auth rejection |
|---|---|---|---|
| `POST /api/auth/register` | CAPTCHA and registration JSON | `201`, account created; no login | CAPTCHA/validation/conflict errors |
| `POST /api/auth/login` | CAPTCHA and login JSON | `200`, user JSON and two cookies | `401 INVALID_CREDENTIALS` |
| `POST /api/auth/otp/request` | Email and CAPTCHA token JSON | Generic `202`; no cookie changes | Validation/CAPTCHA errors; IP-wide `429 OTP_LOGIN_RATE_LIMITED` |
| `POST /api/auth/otp/verify` | Email and six-digit code string JSON | `200`, user JSON and the normal two session cookies | Generic `401 INVALID_OTP_LOGIN_CODE`; validation `400`; IP-wide `429`; cookies preserved |
| `GET /api/auth/me` | Valid access cookie | `200`, current profile | `401 UNAUTHORIZED` |
| `POST /api/auth/refresh` | Refresh cookie; access cookie may be missing, expired or blacklisted | Empty `200`, replacement cookies | `401 INVALID_REFRESH_TOKEN`, both cookies deleted |
| `POST /api/auth/logout` | Valid access cookie; optional presented refresh cookie | Empty `204`, both cookies deleted | `401 UNAUTHORIZED`, no cookie deletion |
| `POST /api/auth/forgot-password` | Email JSON; anonymous, no CAPTCHA required | `202`, generic acknowledgement; no cookie changes | `400` validation; client-IP-wide `429` |
| `POST /api/auth/reset-password` | Email, six-digit code string and new password JSON; anonymous | Empty `204`, both cookies deleted; no automatic login | Generic `422 INVALID_PASSWORD_RESET_CODE`; `400` validation; client-IP-wide `429`; cookies preserved |

Refresh and logout accept an empty JSON object. Credentials supplied in JSON or the query
string are not used. The API does not accept bearer headers as a replacement for the access
cookie. Authentication failures and expected handler errors use ProblemDetails with `code`
and `traceId`; `traceId` matches `X-Request-Id`.

Whenever either session cookie accompanies an unsafe request, send JSON and
`X-Requested-With: bandup`. This also applies to login/register and password recovery when old
cookies are present.
A failed CSRF guard returns `400 VALIDATION_ERROR` before the handler and leaves cookies and
sessions unchanged. Malformed/missing JSON is a request error, not a refresh attempt.

For a separate frontend origin, use the one configured in `Cors:AllowedOrigin` and send
credentials. The browser's SameSite/Secure rules still apply; CORS does not override them.

```javascript
async function sessionPost(apiOrigin, action) {
  const response = await fetch(`${apiOrigin}/api/auth/${action}`, {
    method: 'POST',
    credentials: 'include',
    headers: {
      'Content-Type': 'application/json',
      'X-Requested-With': 'bandup'
    },
    body: '{}'
  });
  if (!response.ok) {
    throw await response.json();
  }
  // Refresh's 200 and logout's 204 have no JSON response body.
}
```

This helper is transport only. Put the following coordination around calls to it rather than
calling refresh independently from every component. JavaScript does not need to read, store
or decode the raw session tokens.

## Coordinate refresh and logout

1. Share one in-flight refresh operation among concurrent requests. Coordinate across tabs
   that share the browser cookies, as well as components in one tab.
2. On a protected request's `401`, complete that shared refresh and retry the request once.
   A failed refresh requires sign-in; do not create an endless refresh/retry loop.
3. When logging out, stop new refresh attempts and wait for the current one to finish. The
   subsequent logout must use the latest browser cookies. If the access token is expired,
   complete renewal before logout; this API has no anonymous logout path.
4. After successful logout, stop authenticated work and clear client-side user/profile state.
   A late request must not start another refresh and restore the session.

The server serializes session writes by user. It deliberately uses strict reuse detection:

| Server order | Result |
|---|---|
| Two refreshes present the same old token | First returns `200`; second detects reuse, returns `401` and revokes the successor plus other refresh sessions |
| Logout commits before refresh | Logout returns `204`; refresh detects the revoked credential, returns `401` and revokes remaining refresh sessions |
| Refresh commits before logout, but logout still presents the old cookies | Refresh returns `200`; logout returns `204` and blacklists the old access JWT. The new refresh token/JWT survive because logout targets only the presented credential |

Locks provide a consistent database order; they do not control HTTP response arrival or
browser cookie updates. Waiting for renewal before sending logout avoids the final row's
stale-cookie race. Logout normally revokes one presented refresh token; reuse detection
revokes all of the user's unrevoked refresh tokens, including other devices. Reuse does not
blacklist every access JWT already issued to those devices.

## Password recovery

Request the code and submit it as a string with the account email and new password. Show the
same acknowledgement for every `202`; do not infer account existence or delivery success.
Map `422 INVALID_PASSWORD_RESET_CODE` to the recovery form without triggering session refresh.
Malformed input uses `400`, and client-IP throttling uses `429`.

After a successful reset, clear client-side user/profile state and require normal login.
Coordinate reset with in-flight refresh calls across tabs so a late response cannot restore
cookies after the reset response. All refresh tokens stored before the reset are revoked;
already-issued access JWTs retain their normal expiry. This browser's two cookies are deleted
only on successful commit. Rejected reset requests and infrastructure errors preserve cookies.

Recovery does not require CAPTCHA or an authenticated session. The existing CSRF guard still
applies whenever either session cookie is sent. See [local setup](auth-local-setup.md#6-test-forgotten-password-recovery-with-a-local-inbox)
for the Swagger/Mailpit walkthrough and [ADR 0017](adr/0017-email-password-reset-contracts-and-policy.md)
for code lifetime, attempt/resend limits, delivery guarantees and access-token policy.

## Email OTP sign-in

Both routes are available in Swagger. Password login remains available. This flow signs in
an existing active account without a password; it does not enroll a second factor.

| Route | Client JSON | Success | Rejection |
|---|---|---|---|
| `POST /api/auth/otp/request` | Email and CAPTCHA token | Generic `202`; no cookie changes | Validation/CAPTCHA errors; IP-wide `429 OTP_LOGIN_RATE_LIMITED` |
| `POST /api/auth/otp/verify` | Email and six-digit code string | `200`, public user JSON and the normal two session cookies after commit | Generic `401 INVALID_OTP_LOGIN_CODE`; validation `400`; IP-wide `429`; cookies preserved |

The verification body is `{ "email": "learner@example.com", "code": "012345" }`.
Requesting the email is a separate operation with
`{ "email": "learner@example.com", "captchaToken": "fake-captcha-token" }` in local Fake mode.
Client IP and User-Agent come from HTTP connection/headers, never client JSON. Extra JSON
fields do not override server metadata or supply passwords/session credentials.

Show the same acknowledgement for every `202`; it does not confirm eligibility or delivery.
Display OTP verification's `401` on the login form without calling refresh or automatically
retrying the code. Refresh-and-retry applies to protected resource requests, not a failed
login credential. Keep the existing JSON/CSRF headers when cookies accompany either request.
This flow is for existing active accounts and does not auto-register users or award credits.
See [ADR 0018](adr/0018-email-otp-login-contracts-and-policy.md) for limits, purpose separation
and commit semantics. See [the setup guide](auth-local-setup.md#8-sign-in-with-an-email-otp)
for configuration and a complete Swagger walkthrough.

Codes expire in ten minutes, permit five incorrect guesses and can be requested again after
60 seconds, subject to independent email/IP window quotas. A new code supersedes the old
login challenge without affecting password-reset challenges. After `200`, fetch `/me` for
the actual credit balance; the login response keeps the existing balance placeholder.
Password reset invalidates all pending login challenges and revokes existing refresh sessions
atomically. Already-issued access JWTs on other devices retain their normal expiry.

## Failure behavior

- Missing, unknown, expired unrevoked credentials and inactive users cannot refresh. A known
  revoked token triggers reuse handling even if expired or its owner is inactive.
- Missing, unknown, foreign or already-revoked refresh cookies do not prevent logout from
  blacklisting the caller's valid access JWT. A foreign cookie never revokes its owner's row.
- A Redis blacklist read outage logs a warning and permits otherwise-valid JWTs. Signature,
  issuer, audience, lifetime and required claim validation still run. Known blacklist entries
  reject JWTs through expiry plus the validator's 30-second clock skew.
- A Redis write failure aborts logout, rolls back database revocation and returns a generic
  `500` without deleting cookies. A database save/commit failure after the Redis write can
  leave the access JWT blacklisted while refresh revocation rolls back. The response remains
  `500` with no cookie change. Recovery may need a fresh refresh or sign-in.
- Infrastructure failures do not write replacement cookies or clear them as though logout
  succeeded. Clients should display the request reference and use the response status/code to
  decide what to do, rather than treating every failure as successful logout.

See [ADR 0016](adr/0016-refresh-logout-contracts-and-commit-outcomes.md) for transaction and
locking decisions, and [local setup](auth-local-setup.md) for Swagger and fake CAPTCHA.
