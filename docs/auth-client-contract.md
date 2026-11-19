# Browser session contract

The API owns `bu_at` and `bu_rt` as host-only `HttpOnly` cookies with `Path=/` and
`SameSite=Strict`. `Auth:RequireSecureCookies` controls `Secure`. Login and refresh set cookie
expiry to the issued token expiry; successful logout and rejected refresh delete both cookies.
Names can be configured, so clients should treat the cookies as browser-managed credentials.

## Requests and responses

| Route | Credentials | Success | Expected auth rejection |
|---|---|---|---|
| `POST /api/auth/register` | CAPTCHA and registration JSON | `201`, account created; no login | CAPTCHA/validation/conflict errors |
| `POST /api/auth/login` | CAPTCHA and login JSON | `200`, user JSON and two cookies | `401 INVALID_CREDENTIALS` |
| `GET /api/auth/me` | Valid access cookie | `200`, current profile | `401 UNAUTHORIZED` |
| `POST /api/auth/refresh` | Refresh cookie; access cookie may be missing, expired or blacklisted | Empty `200`, replacement cookies | `401 INVALID_REFRESH_TOKEN`, both cookies deleted |
| `POST /api/auth/logout` | Valid access cookie; optional presented refresh cookie | Empty `204`, both cookies deleted | `401 UNAUTHORIZED`, no cookie deletion |

Refresh and logout accept an empty JSON object. Credentials supplied in JSON or the query
string are not used. The API does not accept bearer headers as a replacement for the access
cookie. Authentication failures and expected handler errors use ProblemDetails with `code`
and `traceId`; `traceId` matches `X-Request-Id`.

Whenever either session cookie accompanies an unsafe request, send JSON and
`X-Requested-With: bandup`. This also applies to login/register when old cookies are present.
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
