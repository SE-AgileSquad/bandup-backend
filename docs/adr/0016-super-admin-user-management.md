# 0016 — Super-admin-only user management and account deactivation

**Status:** Accepted.

## Decision

- Identity has three roles: `User`, `Admin` (teacher/content editor), and `SuperAdmin`.
- Only active super admins can list, create, change roles or deactivate users. Content
  management accepts active admins and super admins. These checks use the current database
  role, extending the live role-check approach already implemented for the admin panel.
- Every validated JWT also checks that its account still exists and is active. Deactivation
  therefore invalidates old access tokens on subsequent requests, including learner routes.
  This is an intentional database read per authenticated request.
- User deletion means deactivation. User data, attempts and financial history remain intact;
  refresh tokens are revoked in the deactivation transaction.
- User-management mutations share the existing transaction-scoped advisory lock and recheck
  the actor after acquiring it. The last active super admin and the caller's own super-admin
  access cannot be removed through these APIs.
- The migration adds the role value without promoting existing admins. A database operator
  explicitly bootstraps the first super admin; later changes use the API.
- Credit-adjustment APIs remain deferred until the billing changes are available.

## Consequences

An admin role claim alone grants no user-management permission, and stale tokens cannot retain
old admin privileges. Frontend visibility must follow the same role split. See
[admin-users-api.md](../admin-users-api.md) for HTTP contracts and bootstrap steps.
