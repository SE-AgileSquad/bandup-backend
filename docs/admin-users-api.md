# Admin users API

The admin frontend must show Users only to `SuperAdmin`. Every `/api/admin/users` endpoint
also enforces that rule on the server using the caller's current database role, so hiding
the page is not the access control. `Admin` is a teacher/content editor; both `Admin` and
`SuperAdmin` may manage passages and lessons. `User` cannot access the admin workspace;
ordinary learner endpoints and learner login remain available to active users.

## Session and errors

Use the existing `bu_at` authentication cookie. Mutations must send
`X-Requested-With: bandup` and `Content-Type: application/json`, including DELETE (send `{}`).
Missing, invalid or deactivated sessions return 401; an active caller without the current
super-admin role receives 403. Role changes take effect with existing tokens.
Expected application errors use the existing ProblemDetails response with `code` and `traceId`.

## List users — `Admin_ListUsers`

`GET /api/admin/users?page=1&pageSize=20&role=Admin&search=sara&isActive=true`

| Parameter | Contract |
|---|---|
| `page` | Default 1, clamped to at least 1. |
| `pageSize` | Default 20, clamped to 1–100. |
| `role` | Optional: `User`, `Admin`, `SuperAdmin`. Omit for all roles. |
| `search` | Optional substring of name or email, case-insensitive, at most 320 characters. |
| `isActive` | Defaults to true; false lists deactivated accounts. |

Filters apply before both counting and pagination. Ordering is email, then user ID.

```json
{
  "items": [
    {
      "id": "019aef00-0000-7000-8000-000000000001",
      "displayName": "Sara Ahmadi",
      "email": "sara@example.com",
      "role": "User",
      "isActive": true,
      "createdAt": "2026-11-22T12:00:00Z",
      "creditBalance": 3
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 1
}
```

`creditBalance` is null for an existing user without an account. The current model has no
phone number; name and email are used for this release. Password hashes and tokens never
appear in the list. Invalid role/search input returns 400.

## Create user — `Admin_CreateUser`

`POST /api/admin/users`

```json
{
  "email": "teacher@example.com",
  "displayName": "Teacher",
  "password": "ExamplePassword123",
  "role": "Admin"
}
```

All four fields are required. Any of the three roles is allowed. Email is limited to 320
characters, name to 60, and the password to 10–128 with at least one letter and one digit,
matching registration. The password is hashed. This does not create a login session for
the new user or replace the super admin's cookies.

Returns 201 with `{ "id": "..." }`, 400 for validation errors, or 409 `EMAIL_TAKEN` for a
duplicate email (case-insensitive). The user and an account opened by the existing signup
credit service (currently 3 credits) are committed in one transaction. Manual adjustments
use the separate endpoint below.

## Change role — `Admin_ChangeUserRole`

`PATCH /api/admin/users/{userId}/role`

```json
{ "role": "SuperAdmin" }
```

Returns 204, including when the role is already the requested value. Returns 400 for an
unsupported/missing role, 404 for a missing user, or 409 when demoting the last active super
admin or attempting self-demotion. Changing the role does not reactivate a deactivated user.

## Delete (deactivate) — `Admin_DeactivateUser`

`DELETE /api/admin/users/{userId}` with JSON body `{}`.

Returns 204, including when already deactivated. Returns 404 for a missing user, or 409 for
self-deactivation or deactivating the last active super admin. Users, attempts and credit
history are retained. Refresh tokens are revoked and existing access tokens are rejected
on subsequent authenticated requests. Deactivated accounts cannot log in and disappear
from the default active-users list.
Their email remains reserved; deactivation does not allow another account to reuse it.

## Credit balance and history

All credit routes require the same current SuperAdmin policy as the Users page; an ordinary
Admin or User receives 403. GETs do not require the mutation CSRF header.

- `GET /api/admin/users/{userId}/credits` (`Admin_GetUserCredits`) returns 200 with
  `{ userId, displayName, email, balance }`. balance is null if the user has no credit account.
  Fetch this when opening the modal so the preview starts from the latest stored balance.
- `GET /api/admin/users/{userId}/credits/history?page=1&pageSize=20`
  (`Admin_ListUserCreditHistory`) returns a paged result ordered by createdAt descending,
  then ID descending. Each item has id, delta, reason, referenceId, createdByAdminId, note,
  createdAt. All ledger movements are included, including signup and any writing movements.
  Paging uses the standard clamped page/pageSize limits; an out-of-range page is empty.
  An existing user without an account/history has an empty history.

Both return 404 for a missing user. They can also read deactivated users' retained records.

## Adjust credits — `Admin_AdjustUserCredits`

`POST /api/admin/users/{userId}/credits/adjustments`

```json
{
  "delta": 3,
  "note": "Additional writing practice",
  "requestId": "019b0000-0000-7000-8000-000000000001"
}
```

delta is a required signed non-zero integer. It is not limited to the +1/+3/+5 shortcut
buttons in the design. Positive grants credits and negative deducts them. The account uses
32-bit integer credit units: decimal values are rejected, and the resulting balance must be
between 0 and 2147483647. The minimum signed integer cannot be a valid deduction and is
rejected. note is optional, at most 200 characters; it is trimmed and blank becomes null.
requestId is a required non-empty UUID generated by the client once per confirmed adjustment.
POST requires `X-Requested-With: bandup` and JSON.

Returns 200:

```json
{
  "adjustmentId": "019b0000-0000-7000-8000-000000000002",
  "userId": "019aef00-0000-7000-8000-000000000001",
  "delta": 3,
  "note": "Additional writing practice",
  "balance": 6,
  "createdAt": "2026-11-26T10:00:00Z",
  "isReplay": false
}
```

Use balance from the response to refresh the user row and confirmation message. The modal's
arithmetic preview and suggested buttons are frontend behavior; the server checks the current
locked balance again. Another adjustment may have changed it since the preview.

Every successful adjustment uses the existing CreditService/CreditAccount aggregate, records
an `AdminAdjust` ledger entry with its signed delta, note, current super-admin ID and requestId
as referenceId, and updates the balance in the same transaction. This works for accounts of
any role, including the actor's own account or a deactivated target. Missing users return 404.
A missing credit account returns 409 rather than creating an account or granting signup
credits implicitly. The existing signup flow opens accounts for newly created users.

Retries must reuse the same requestId and the same user, actor, delta and normalized note.
A successful retry returns the original adjustmentId/createdAt and isReplay true without
changing balance or writing another entry. balance is always the current account balance,
so a later retry may report a different balance after other transactions. Reusing a requestId
with a different payload, target or actor returns 409. An operation with new intent needs a
new requestId. Failed operations do not reserve their requestId.

Invalid input returns 400 with field-keyed errors. A negative or overflowing resulting balance
returns 409 with a delta field error and writes nothing. Concurrent adjustments acquire the
existing role-change transaction lock (and recheck the actor's role), then lock/re-read the
account row. The role lock also serializes first-time request-ID checks across admin targets;
the existing unique reason/reference index provides a database backstop. Other balance writers
must use the same account-row locking protocol. In particular, a balance read must not be used
later for an unlocked read-modify-write. No learner Writing charging/refund API is added here.

No migration or new table is required: the existing ledger already stores actor, note and
reference ID and has the unique index needed for retries.

## Migration and first super admin

`AddSuperAdminRole` extends the existing `identity.users` role CHECK constraint; no new role
table is needed. Existing `User` and `Admin` rows retain their roles. It does not automatically
grant elevated privileges to any existing admin.

Apply through the normal explicit migration command:

```shell
dotnet run --project src/BandUp.Api -- --migrate
```

One-time bootstrap: a database operator chooses an existing active account, replaces the
example email below, and executes this transaction. The returned row confirms the chosen
account; zero rows means the email did not match an active user or a super admin already exists.

```sql
BEGIN;
SELECT pg_advisory_xact_lock(42411, 1);
UPDATE identity.users
SET role = 'SuperAdmin'
WHERE email = 'replace-with-the-chosen-email@example.com'
  AND is_active
  AND NOT EXISTS (
    SELECT 1 FROM identity.users WHERE role = 'SuperAdmin' AND is_active
  )
RETURNING id, email, role;
COMMIT;
```

After bootstrap, create/promote other super admins through the API. The database role is
checked on each request, so an existing valid session can use the new permissions immediately.
Before downgrading this migration, explicitly reassign all `SuperAdmin` rows to a supported
old role; otherwise the old CHECK constraint correctly prevents the downgrade.
