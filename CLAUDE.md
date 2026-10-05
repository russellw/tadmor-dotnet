The goal of this project is to develop comprehensive business management software.
It is the C# counterpart of tadmor (~/tadmor): the same product, specified by
tadmor's spec/ and checked by its conformance/ suite, built on a different stack so
the two can be compared (see ~/tadmor/docs/counterpart-metrics.md).

Technology stack:
Postgres for the database, using the shared schema from tadmor's db/migrations.
C# on .NET 10 and ASP.NET Core for the back end, with Entity Framework Core over Npgsql.
Server-rendered Razor Pages for the user interface; no npm, no JavaScript build, no Blazor.
MSTest for tests.
See docs/stack.md for the decision and its rationale.

Schema design:
The schema is shared with tadmor and is not ours to redesign. EF Core entities map onto
the existing tables and views; EF Core migrations are not used for it. Our own migration
runner applies db/migrations.

Dependencies:
Supply-chain conscious throughout; keep the third-party footprint small, pinned, and
reviewable in-repo. ASP.NET Core comes from the SDK's shared framework, not NuGet.
The only permitted NuGet packages are those listed in docs/stack.md. New packages need
a conversation first. Packages are pinned by lockfile and vendored under vendor/; the
build restores offline from there and never contacts nuget.org.

Version control:
Commit directly to the default branch. Do not create feature branches.
