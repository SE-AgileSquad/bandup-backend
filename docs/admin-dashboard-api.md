# Admin dashboard API

`GET /api/admin/dashboard` — endpoint name `Admin_GetDashboard`.

Returns the four counters at the top of Admin home in a single response. Requires an active
`Admin` or `SuperAdmin` account, using the existing authentication cookie and current database
role checks. An anonymous/invalid/inactive session receives 401; an ordinary active `User`
receives 403.

```json
{
  "publishedPassages": 24,
  "draftPassages": 6,
  "totalLessons": 18,
  "totalUsers": 128
}
```

- `publishedPassages`: passages currently in `Published` status.
- `draftPassages`: passages currently in `Draft` status. Archived passages count in neither card.
- `totalLessons`: all lessons, both draft and published.
- `totalUsers`: all accounts across all three roles, including deactivated accounts. The value
  is visible to both admin roles. Unlike the default Users list, this is not an active-only count.

Empty sets return 0. Counts are read from the database on each request without caching or
loading entity lists. The four reads are sequential and do not promise a transaction snapshot
across concurrent writes.

No query parameters or request body are required. User-management endpoints remain restricted
to `SuperAdmin`; the frontend must show the Manage users action only to that role.

This API does not include recent activity, and requires no schema change or migration.
