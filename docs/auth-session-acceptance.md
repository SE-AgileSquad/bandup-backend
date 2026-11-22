# Refresh/logout acceptance

This branch implements AUTH-09/AUTH-10 on top of registration, login, profile and signup
credits. It uses the existing schema and packages. Phase 6 adds HTTP race acceptance and
setup/client documentation; production behavior remains the contract in
[ADR 0016](adr/0016-refresh-logout-contracts-and-commit-outcomes.md).

## Reproduce verification

Verified on 2026-11-17: Release build completed with zero warnings/errors; **258 tests passed**,
zero failed or skipped. `git diff --check` passed. The documentation link check resolved all
local references in the changed documentation.

With a restored checkout and Docker's Linux engine available, run from the repository root:

```powershell
dotnet build -c Release --no-restore
dotnet test -c Release --no-build --no-restore
git diff --check
```

On a fresh checkout, restore dependencies before using `--no-restore`. Testcontainers creates
its own PostgreSQL, Redis and RabbitMQ instances. It does not use the developer's compose
volumes, stored passwords, signing key or CAPTCHA keys. The migration CLI check creates one
additional database inside that disposable PostgreSQL container.

## Acceptance matrix

| Behavior | Evidence |
|---|---|
| Real `--migrate` argument applies all migrations and exits instead of starting the API | `MigrationTests.INF_10_Api_Migrate_Argument_Applies_The_Schema_And_Exits` launches the built assembly with isolated configuration |
| Layering and transaction pipeline contracts remain valid | Architecture tests, `PipelineTests`, `TransactionTests` |
| Reuse rejection commits revocation; ordinary failed results roll back | `RefreshTransactionTests` and real-handler `RefreshHandlerTests` |
| Rotation stores only hashes, preserves the replacement chain and uses current role/configured lifetimes | `SessionTokenIssuerTests`, `RefreshHandlerTests`, `AuthSessionHttpTests` |
| Missing/unknown/expired/inactive credentials do not issue tokens; expired revoked credentials still trigger reuse | Unit and integration `RefreshHandlerTests`; HTTP rejection cases |
| Rotation/revocation require Read Committed transactions and refresh stale tracked state after locking | `SessionStoreTests` |
| Login → profile → refresh → profile → logout → unauthorized works with a cookie jar | `AuthSessionHttpTests`, including HTTP/HTTPS policy and custom cookie names |
| Cookie expiry matches issued metadata; issuance and deletion share name/path/security scope | `AuthSessionHttpTests` checks response headers and actual persisted expiry |
| Refresh works with absent, expired, malformed or blacklisted access cookies | `AuthSessionHttpTests.AUTH_09_Refresh_Works_Without_A_Valid_Access_Token` |
| Credentials in body/query or bearer headers do not replace cookie credentials | `AuthSessionHttpTests`, `LogoutTests` |
| Logout revokes only the presented owned refresh row and rejects copied access JWTs afterward | `LogoutTests` and HTTP cookie-jar/copy cases |
| Missing/foreign refresh cookies still allow access-token blacklisting | Unit `LogoutHandlerTests`, `LogoutTests`, HTTP logout cases |
| Invalid signature/lifetime/required claims fail before blacklist lookup | `CurrentUserTests`, `LogoutTests` |
| Blacklist retention covers JWT expiry plus clock skew | `AccessTokenRevocationStoreTests`, Redis TTL and clock-skew integration cases |
| Redis reads fail open only after other JWT validation; writes fail logout | `AccessTokenRevocationStoreTests`, `LogoutTests` |
| Save/commit failures roll back database changes; a prior Redis blacklist write survives | `LogoutTests`; HTTP infrastructure failures send no replacement/deletion cookies |
| Missing CSRF requirements and malformed/empty JSON cannot mutate sessions | `CsrfTests`, `AuthSessionHttpTests` |
| ProblemDetails codes, request ids and OpenAPI status/body metadata agree with HTTP | `AuthSessionHttpTests` and foundation request/error tests |
| Swagger page and scripts are served by the API; CSRF helper is Development-only | Swagger asset/initialization tests in `AuthSessionHttpTests` |
| Two concurrent HTTP refreshes issue one successor; the loser commits reuse revocation before its `401` | `AuthSessionRaceTests.AUTH_09_Concurrent_Http_Refresh_Commits_One_Successor_Then_Revokes_All_Owned_Sessions` |
| Logout-first and refresh-first request orders follow the presented-credential contract | `AuthSessionRaceTests.AUTH_09_10_Refresh_And_Logout_Serialize_The_Presented_Credentials` |
| Another user's refresh is independent of a held session lock | HTTP `AuthSessionRaceTests` and adapter `SessionStoreTests` |

Race tests hold a real per-user PostgreSQL lock, start a second HTTP request, and observe its
actual `pg_stat_activity` lock wait before releasing the first transaction. They do not infer
serialization from arbitrary sleep durations. A separate test proves independent users do
not wait for the held lock.

## Contract limits to review

Password-recovery coverage is added in `PasswordRecoveryHttpTests` (AUTH-15). It exercises
registration/login, generic code requests, reset and cookie deletion, copied refresh rejection,
old/new password login, code reuse, generic ineligible-state rejection and committed wrong
attempts. HTTP/HTTPS cookie policy and custom names are checked with cookie jars. Anonymous
recovery is also exercised through real disposable Mailpit, Redis and PostgreSQL.

Additional cases verify JSON/credential validation, CSRF with either session cookie, quota
mapping/outage, connection IP versus untrusted forwarding/body input, save/commit rollback
without cookie changes or email, sensitive-data absence in logs, API startup validation,
Worker startup without a recovery key, and OpenAPI request/response metadata. The real email
provider and real inbox remain phase 6; no production delivery is claimed here.

Recovery phase 5 verification on 2026-11-22 passed **464 tests** (222 unit, 11 architecture,
231 integration), zero failed/skipped, and a serial Release build with zero warnings/errors.
The exact successful verification commands were:

```powershell
dotnet build -c Release --no-restore --disable-build-servers -m:1
dotnet test --project tests/BandUp.UnitTests/BandUp.UnitTests.csproj -c Release --no-build --no-restore
dotnet test --project tests/BandUp.ArchitectureTests/BandUp.ArchitectureTests.csproj -c Release --no-build --no-restore
dotnet test --project tests/BandUp.IntegrationTests/BandUp.IntegrationTests.csproj -c Release --no-build --no-restore
docker compose --env-file infra/env/dev.env.example -f infra/docker/docker-compose.dev.yml --profile recovery config --quiet
```

- The client must share refresh work and coordinate logout across components/tabs. If refresh
  wins first and logout presents the old cookies, the new session survives. This is verified
  behavior, not a claim that row locks order HTTP cookie writes.
- Reuse revokes all refresh sessions for the user, including the winner's successor and other
  devices. It does not invalidate every access JWT already issued to the user.
- Redis read outage is fail-open. Redis writes and PostgreSQL commits are independent, so a
  failed logout can leave a blacklisted access JWT with an unrevoked refresh row.
- Login requires real or fake CAPTCHA; refresh/logout do not. Real Turnstile browser/widget
  interaction, native-browser rendering, production TLS/proxy deployment and enabled Sentry
  are outside this backend acceptance run. The suite verifies HTTP cookie jars, served Swagger
  assets, configuration and fake-provider behavior.

Use [local setup](auth-local-setup.md) for the Swagger walkthrough and
[the client contract](auth-client-contract.md) for request coordination and recovery.
Unrelated review fixes remain listed in [implementation status](implementation-status.md).
