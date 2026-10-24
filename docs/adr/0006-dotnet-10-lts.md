# 0006 — .NET 10 LTS

## Context

The backend is expected to live for years and to be maintained by a small team, possibly with
students joining and leaving. Choosing a short-term support release means a forced upgrade in
the middle of feature work.

We also depend on the ecosystem tracking us: EF Core, Npgsql, Serilog, Sentry, Testcontainers
and the test platform all need to support the runtime we pick.

## Decision

Target **.NET 10 (LTS)** on `net10.0`, with `global.json` pinned to SDK `10.0.100` and
`rollForward: latestFeature`, so a newer feature band of .NET 10 is accepted but .NET 11 is not.

Package versions are pinned exactly in `Directory.Packages.props` (central package management)
and each is verified to support `net10.0` before being added. No floating versions.

The toolchain is pinned too: `dotnet-ef` is a local tool in `.config/dotnet-tools.json` at the
same patch version as EF Core.

## Consequences

- Security patches for years without a planned migration.
- `rollForward: latestFeature` keeps the build working when a developer has a newer .NET 10
  SDK, without silently accepting a major version.
- Exact pinning makes builds reproducible and upgrades deliberate.
- Cost: an exact pin means someone must move it. NuGet audit warnings break the build, by
  design, so a vulnerable dependency cannot sit unnoticed — but that also means an audit
  advisory can block work until it is triaged.
- Cost: every new package needs a `net10.0` verification step before it is added.
