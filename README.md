# tadmor-dotnet

The C# counterpart of [tadmor](https://github.com/russellw/tadmor): the same business
management software, specified by tadmor's `spec/` and checked by its
`conformance/` suite, built on .NET so the stacks can be compared (see
tadmor's `docs/counterpart-metrics.md`).

ASP.NET Core on .NET 10 serves both the JSON API and a server-rendered
Razor Pages UI, with Entity Framework Core over Npgsql on Postgres 17. See
[`docs/stack.md`](docs/stack.md) for the decision, the measured dependency
trees, and the supply-chain setup.

## Layout

```
src/Tadmor/          the server (also the adduser and resetdb commands)
tests/Tadmor.Tests/  MSTest tests (unit, and integration against TEST_DATABASE_URL)
db/migrations/       the shared schema, copied from tadmor (spec/UPSTREAM)
spec/, conformance/  the specification and black-box suite, copied from tadmor
vendor/nuget/        every NuGet package, committed; restore reads only this
tools/               vendor.py (maintains vendor/), conformance.sh
docs/                stack.md
```

## Prerequisites

- **.NET SDK 10.0.1xx** from the OS (`sudo apt install dotnet-sdk-10.0` on
  Ubuntu 26.04). `global.json` accepts any 10.0.1xx patch.
- **Postgres 17** reachable via `DATABASE_URL`. `make db` starts one in
  podman with the dev, test, and conformance databases.
- **Go**, only to run the conformance suite.

## Configuration

| Env var | Required | Default | Purpose |
| ------- | -------- | ------- | ------- |
| `DATABASE_URL` | yes | — | Postgres URL, `postgres://user:pass@host:port/db?sslmode=disable` |
| `HTTP_ADDR` | no | `:8080` | Listen address, `host:port`; an empty host means every interface |
| `PORT` | no | — | Listen port; overrides `HTTP_ADDR`'s when set |
| `TEST_DATABASE_URL` | for tests | — | Database the integration tests wipe; its name must end in `_test` |

## Build, run, test

```sh
make build        # build the server and tests
make run          # build and run the server (migrates on start)
make test         # run the tests (wipes TEST_DATABASE_URL)
make conformance  # run tadmor's suite against a fresh server (wipes the _conformance DB)
make release      # self-contained linux-x64 server in bin/release
make vendor-check # verify vendor/nuget against vendor/lock.txt (offline)
make vendor-sync  # re-resolve vendor/nuget from nuget.org (online, 7-day cooldown)
```

Create or reset a login (the password is the first line of stdin):

```sh
echo 'a-long-password' | make adduser EMAIL=you@example.com NAME='Your Name'
```

The server applies pending migrations on startup. Endpoints: `GET /healthz`
(liveness) and `GET /readyz` (database reachable).

## Deployment

`make release` produces a self-contained `linux-x64` directory with the
.NET runtime inside, so the target needs no .NET installation. It runs on
Debian 13 (glibc 2.41); see `docs/stack.md`, "Deployment".
