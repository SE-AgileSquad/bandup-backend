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
credit service (currently 3 credits) are committed in one transaction. No manual credit
adjustment is implemented here.

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

## Credit adjustment — proposed, NOT implemented

- Endpoint name: `Admin_AdjustUserCredits`
- Route: `POST /api/admin/users/{userId}/credits/adjustments`
- Required role: `SuperAdmin`
- Proposed JSON: `{ "delta": 3, "note": "Additional writing practice" }`
- Proposed rules: signed non-zero delta, resulting balance cannot be negative, write a
  ledger entry with the actor and note atomically, support idempotent retries.

This route is deliberately not mapped and has no handler or stub. Finalize its input/output
contract with the billing changes before implementing it.

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
