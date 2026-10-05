# Stack

**Status:** adopted 2026-10-05.

## Decision

- **Back end:** ASP.NET Core on .NET 10 (the current LTS), serving both
  the JSON API required by tadmor's `spec/api.md` and the user interface.
  Minimal API endpoints for the JSON API.
- **Data access:** Entity Framework Core over Npgsql, with entities
  written by hand onto the shared schema.
- **User interface:** server-rendered Razor Pages. No SPA, no Blazor, no
  npm, no JavaScript build step. If a small amount of client-side
  behaviour is needed, it is handwritten, or at most one vendored
  JavaScript file (such as htmx), which needs a conversation first.
- **Tests:** MSTest on Microsoft.Testing.Platform (not VSTest).
- **Database:** Postgres 17 with the shared schema from tadmor's
  `db/migrations/`.
- **Toolchain:** the .NET 10 SDK from the operating system's packages
  (Ubuntu 26.04 ships `dotnet-sdk-10.0`, 10.0.112 when this was written;
  the OS is out of scope for the metrics).

## Why ASP.NET Core

ASP.NET Core is not a NuGet package. It ships with the SDK as the
`Microsoft.AspNetCore.App` shared framework, which includes Kestrel,
routing, `System.Text.Json`, cookie authentication, data protection,
`PasswordHasher`, Razor, and logging. The mainstream .NET framework
therefore costs **no dependencies and no maintainers** beyond the
toolchain's publisher. As with Django in tadmor-python, and unlike Laravel
or Spring Boot, the representative choice and the lean choice are the
same thing. No alternative (Carter, FastEndpoints)
does anything the shared framework does not.

## Why Razor Pages

Server-rendered Razor (MVC views and Razor Pages) is by far the most
widely deployed .NET web UI, and Razor Pages is Microsoft's recommended
form of it for page-oriented applications. It is a good fit for a product
that is mostly forms and lists, and it keeps the comparison with the
siblings' server-rendered templates like for like.

Blazor was considered. It is also in the shared framework and is
Microsoft's headline UI framework for new projects, but it is less widely
used, and its interactive server mode keeps per-user state on the server
behind a WebSocket, which adds moving parts and complicates the Content
Security Policy. Blazor WebAssembly would add NuGet packages and a build
workload.

## Why Entity Framework Core

EF Core is how .NET business software usually talks to a database. Unlike
JPA in tadmor-java, where Hibernate would have added about five
identities, EF Core comes from Microsoft and its Postgres provider from
the Npgsql project, who also publish the driver we need anyway. It
therefore adds **no new organizations**, only four Microsoft packages and
the provider.

EF Core fits the shared schema: entities can map tables, views (keyless
entity types), and generated columns (computed, read-only). Postgres
returns database-generated values through `RETURNING`, so the triggers
need no special handling. Posting and reporting will still be largely
SQL over the views, run through EF Core's raw SQL support.

The rejected alternative was Npgsql alone (one package, two NuGet
accounts besides Microsoft's), which would have saved four packages but
no organizations.

**Entities are written by hand.** The scaffolding route
(`Microsoft.EntityFrameworkCore.Design` plus the `dotnet-ef` tool) would
add 18 packages to the build, including Roslyn, `Newtonsoft.Json`,
`Humanizer.Core`, and `Mono.TextTemplating` (two individual
maintainers). The schema has 43 tables and 19 views. Writing their
mappings is a modest one-time cost, and the result is our own reviewed
code.

## Why MSTest

xUnit is the most popular .NET test framework, but MSTest is
Microsoft's, so its whole tree is Microsoft packages. The `MSTest`
metapackage pulls in VSTest, code coverage and TRX reporting (17
packages). Referencing only `MSTest.TestFramework` and
`MSTest.TestAdapter`, with `EnableMSTestRunner`, runs the tests on
Microsoft.Testing.Platform as a plain executable, with 9 packages.

## Measured trees

Resolved on 2026-10-05 with the .NET 10.0.112 SDK into an empty NuGet
cache (linux/x64), reading each project's `packages.lock.json`. Owners
are each package's NuGet owner accounts. Packages that the shared
framework already provides, such as `Microsoft.Extensions.*`, are pruned
from the graph by the .NET 10 SDK and are not downloaded.

**Runtime (6 packages)**

| Package | Version | Published | NuGet owners |
| ------- | ------- | --------- | ------------ |
| Npgsql.EntityFrameworkCore.PostgreSQL | 10.0.3 | 2026-07-10 | Npgsql, roji |
| Npgsql | 10.0.3 | 2026-05-27 | Npgsql, roji, brar, ninofloris |
| Microsoft.EntityFrameworkCore | 10.0.12 | 2026-09-08 | Microsoft, aspnet, dotnetframework, EntityFramework |
| Microsoft.EntityFrameworkCore.Abstractions | 10.0.12 | 2026-09-08 | (same) |
| Microsoft.EntityFrameworkCore.Analyzers | 10.0.12 | 2026-09-08 | (same) |
| Microsoft.EntityFrameworkCore.Relational | 10.0.12 | 2026-09-08 | (same) |

The provider only requires EF Core 10.0.4, and NuGet resolves the lowest
version that satisfies it. We reference `Microsoft.EntityFrameworkCore.Relational`
10.0.12 directly so that Microsoft's patch releases are taken.

**Test only (9 packages)**

| Package | Version | NuGet owners |
| ------- | ------- | ------------ |
| MSTest.TestFramework | 4.4.1 | Microsoft, MSTestFramework |
| MSTest.TestAdapter | 4.4.1 | Microsoft, MSTestFramework |
| MSTest.Analyzers | 4.4.1 | Microsoft, MSTestFramework |
| Microsoft.Testing.Platform | 2.4.1 | Microsoft, MSTestFramework |
| Microsoft.Testing.Platform.MSBuild | 2.4.1 | Microsoft, MSTestFramework |
| Microsoft.Testing.Extensions.Telemetry | 2.4.1 | Microsoft, MSTestFramework |
| Microsoft.Testing.Extensions.TrxReport.Abstractions | 2.4.1 | Microsoft, MSTestFramework |
| Microsoft.TestPlatform.ObjectModel | 18.9.0 | Microsoft, vstest |
| Microsoft.ApplicationInsights | 2.23.0 | Microsoft, AppInsightsSdk |

The telemetry extension is required by the adapter. Test runs set
`TESTINGPLATFORM_TELEMETRY_OPTOUT=1` (and the CLI's
`DOTNET_CLI_TELEMETRY_OPTOUT=1`), so nothing is sent.

**Release only: runtime packs (3 packages, toolchain)**

`Microsoft.NETCore.App.Runtime.linux-x64`,
`Microsoft.AspNetCore.App.Runtime.linux-x64` and
`Microsoft.NETCore.App.Host.linux-x64`, 10.0.12, owned by Microsoft's
accounts (see Deployment below). These are the .NET runtime itself, not
libraries, and count with the toolchain.

**Totals**

| | runtime | test | everything |
| --- | ---: | ---: | ---: |
| Packages | **6** | 9 | 15 (+3 runtime packs) |
| NuGet owner accounts | **8** | 4 (3 new) | 11 |
| Organizations | **2** | 1 | 2 (Microsoft, Npgsql) |

Microsoft publishes under several organizational accounts (`Microsoft`,
`aspnet`, `dotnetframework`, `EntityFramework`, `MSTestFramework`,
`vstest`, `AppInsightsSdk`), so the account count overstates the
distinct parties trusted. Counted as tadmor counts Go modules, by
organization, the runtime is **two identities** (Microsoft, the Npgsql
project), the same number as tadmor's Go backend. `tools/measure.py` in
tadmor does not yet handle NuGet, and these figures are a careful hand
count until it does.

## Permitted packages

The 15 packages above at the listed versions, and the three runtime
packs for release builds. Nothing else without a conversation first. In
particular:

- **No `Microsoft.EntityFrameworkCore.Design` or `dotnet-ef`** (above).
  EF Core migrations are not used. Our own runner applies
  `db/migrations/`.
- **No `MSTest` metapackage, xUnit, NUnit, FluentAssertions, Moq, or
  Testcontainers.** Tests use `TEST_DATABASE_URL`, as tadmor does.
- **No MailKit.** `System.Net.Mail.SmtpClient` from the base class
  library covers the SMTP that tadmor's `net/smtp` does.
- **No Swashbuckle or other OpenAPI tooling, no Serilog, no AutoMapper,
  no MediatR.**
- **No client-side libraries.** The default Razor Pages template's
  Bootstrap and jQuery are not used.

## Supply-chain posture

Set up and measured on 2026-10-05.

- **Vendored and committed.** Every package is committed, unmodified, as
  its `.nupkg` in `vendor/nuget/` (76 MB, of which the three runtime packs
  are 56 MB). `nuget.config` makes that folder the only package source,
  clears fallback folders and audit sources, and extracts packages into
  the repository's own `.nuget/packages` (ignored by git) rather than
  `~/.nuget/packages`, so nothing cached by another project can stand in
  for a vendored package. `NuGetAudit` is off, because it would fetch
  vulnerability data online. nuget.org is never contacted.
- **Pinned twice.** Exact versions are in `Directory.Packages.props`
  (central package management). Each project's committed
  `packages.lock.json` records every resolved package with NuGet's content
  hash, and `RestoreLockedMode` makes restore refuse any change to the
  lock or any package whose hash differs. `vendor/lock.txt` adds a plain
  SHA-512 of every `.nupkg`, including the runtime packs, which NuGet
  fetches as download dependencies and does not put in the lock files.
- **`tools/vendor.py`** (standard library only) is the whole toolchain.
  `sync` re-resolves everything from nuget.org into an empty folder, with
  the solution's restore and the release's linux-x64 restore. It keeps
  only the packages the lock files and the release name (NuGet also
  downloads versions it considers and then passes over), refuses any
  published less than 7 days ago, then replaces `vendor/nuget` and
  rewrites `vendor/lock.txt`. `check` verifies the feed against
  `vendor/lock.txt` offline, and that every package the lock files name
  is vendored. A dependency change is reviewable as a diff of
  `Directory.Packages.props` and the locks.
- **Install-time code.** NuGet runs no install scripts for SDK-style
  projects, but packages can contribute to the build in two ways, and both
  run with the developer's privileges:
  - MSBuild `.props` and `.targets` files, imported into the build. EF
    Core ships one (`Microsoft.EntityFrameworkCore.props`), and the MSTest
    and Microsoft.Testing.Platform packages ship several.
  - Roslyn analyzers, DLLs loaded into the compiler:
    `Microsoft.EntityFrameworkCore.Analyzers` and `MSTest.Analyzers`.

  All of these are Microsoft's, from the packages above. They cannot be
  blocked without breaking the packages, so they are accepted and
  reviewed with each vendoring.
- **Hermetic build: level 4.** Measured as `docs/counterpart-metrics.md`
  in tadmor describes: two copies of the tree with no `bin/`, `obj/` or
  `.nuget/`, each built (`make release`, then `make build`) in an
  `ubuntu:26.04` container with Ubuntu's `dotnet-sdk-10.0` 10.0.112,
  `--network=none`, and an empty `HOME`. Both builds succeeded, and their
  outputs were byte-identical: all 339 files of the self-contained
  release, and `Tadmor.dll` and `Tadmor.Tests.dll`. `Deterministic` and
  `ContinuousIntegrationBuild` are set in `Directory.Build.props`.
- **Telemetry off.** The Makefile sets `DOTNET_CLI_TELEMETRY_OPTOUT`,
  `TESTINGPLATFORM_TELEMETRY_OPTOUT` and
  `DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE`, so neither the CLI nor
  the test platform phones home.

| Part | Hermetic level | Install-time code | Manual steps |
| ---- | -------------- | ----------------- | ------------ |
| Server and tests | **4** | MSBuild files and analyzers from Microsoft packages (above) | install `dotnet-sdk-10.0` from the OS |
| Release (self-contained linux-x64) | **4** | as above | none beyond the SDK |

## How the application uses ASP.NET Core

- **One process, one binary.** `src/Tadmor` is the server. `adduser` and
  `resetdb` are subcommands of the same executable, as in tadmor-java.
  It reads tadmor's environment variables (`DATABASE_URL`, `HTTP_ADDR`,
  `PORT`). `DATABASE_URL` stays a libpq-style URL, which `Db/PostgresUrl`
  turns into an Npgsql connection string, because Npgsql does not parse
  URLs. Every session runs in UTC, and GSS encryption is off unless the
  URL asks for it, so Npgsql does not go looking for Kerberos libraries.
- **Our own migration runner.** `Db/Migrations` applies the embedded
  `db/migrations/*.up.sql` at startup, as `spec/README.md` describes.
  Npgsql splits each file into statements itself. After applying
  anything, it reloads Npgsql's type cache, because on a fresh database
  that cache was loaded before `citext` existed.
- **EF Core over the shared tables.** `Db/TadmorDb` maps hand-written
  entities. A convention turns property names into the schema's
  snake_case. `citext` columns are mapped as `citext`, so that parameters
  compared with them are sent as `citext`. Sent as `text`, the comparison
  would be case-sensitive (tested).
- **Passwords** use ASP.NET Core Identity's `PasswordHasher`, which is
  in the shared framework (PBKDF2-HMAC-SHA512, its v3 format). The scheme
  is not contract (`spec/domain.md` §12).
- **Tests** are MSTest on Microsoft.Testing.Platform. The test project
  references the `Microsoft.AspNetCore.App` framework as the server does.
  Without that, the SDK does not prune the `Microsoft.Extensions.*`
  packages EF Core asks for, and restores nine of them from NuGet.
  Integration tests wipe `TEST_DATABASE_URL`, whose name must end in
  `_test`.

## Deployment

The SDK from Ubuntu is built from source and carries runtime packs only
for its own RID, `ubuntu.26.04-x64`. A self-contained publish for that
RID links against glibc 2.43, so it will not run on the Debian 13
deployment box (glibc 2.41). A self-contained publish for the portable
`linux-x64` RID needs glibc 2.27, and it was checked in a `debian:13`
container: it starts, migrates, and reports ready. It uses
Microsoft's three runtime packs from NuGet, which are vendored like the
other packages.

The deployable is therefore a self-contained `linux-x64` directory with
the .NET runtime inside (about 110 MB), so the server needs no .NET
installation and no Microsoft package repository. The development
machine runs Ubuntu's build of the same runtime version (10.0.12).

## What would make these choices worth revisiting

- **EF Core getting in the way.** If most data access ends up as raw
  SQL, dropping EF Core for Npgsql alone would remove four packages at
  no cost in organizations.
- **A richer client.** If more screens need client-side behaviour than a
  line editor, htmx is the first candidate to discuss. Blazor static
  rendering with enhanced forms is the in-framework alternative.
- **The runtime packs.** If Debian packages .NET, or the deployment box
  moves to Ubuntu, a framework-dependent publish against the OS runtime
  would drop the three packs.
- **Test tree.** If Microsoft.Testing.Platform drops its telemetry
  dependency, the test tree shrinks by two packages.
