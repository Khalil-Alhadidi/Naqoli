# ZainCash Bill Pay and Transfer Agent

An agent that turns one sentence in Iraqi Arabic into one confirmation card and one tap, against a mock wallet API, for transfers, bills, store purchases and the read-only balance question. It is an entry for the ZainCash Agentic AI Challenge Iraq, topic 04, and its design is in [docs/architecture.md](docs/architecture.md).

Rules: the nine rules that are never broken are listed in [CLAUDE.md](CLAUDE.md).

## Repository layout

- `src/`: the four applications: `Wallet.MockApi` (minimal API, EF Core with SQL Server), `Agent.Core` (orchestrator and request pipeline), `Agent.Web` (Blazor Server with MudBlazor), `Agent.Eval` (console that runs the request sets).
- `tests/`: `Agent.Core.Tests` (xunit.v3 unit and API tests) and `Agent.Web.Journeys` (Playwright screen journeys).
- `data/`: Khalil's exported test-set files: the wallet seed (`data/seed/*.json`, see `data/seed/README.md`) and the request sets.
- `docs/`: architecture, build plan, failure modes and disclosure. The agent's write path as built (structured request, resolvers, checks, pending states, Confirm and Checking, audit log) is in [docs/agent-core.md](docs/agent-core.md).

## Prerequisites

- .NET 10 SDK.
- The `dotnet-ef` tool. It is pinned as a local tool in `.config/dotnet-tools.json` (version 10.0.13); after cloning, run `dotnet tool restore` once.
- A SQL Server: the local default instance, LocalDB, or a container (see below).
- Docker Desktop or Docker Engine, for route B only.
- `pwsh`, used below to install the Playwright browsers, means PowerShell 7 or later ([installing PowerShell on Windows](https://learn.microsoft.com/en-us/powershell/scripting/install/installing-powershell-on-windows)).

### Windows path length

The Microsoft.Playwright package ships deeply nested files. With a clone path longer than roughly 100 characters, `dotnet build` fails with MSB3021 "exceeds the OS max path limit". Clone to a short path such as `C:\src\naqoli`, or enable long paths: turn on the Windows setting `LongPathsEnabled` ([maximum path length limitation](https://learn.microsoft.com/en-us/windows/win32/fileio/maximum-file-path-limitation)) and run `git config --global core.longpaths true`.

## Database

The connection string is configuration, named `Wallet`. The default lives in `src/Wallet.MockApi/appsettings.Development.json` and applies only when the environment is Development, which `dotnet run` uses. It is the local default SQL Server instance with Windows authentication:

```text
Server=localhost;Database=ZainCashWallet;Trusted_Connection=True;TrustServerCertificate=True
```

If you have that, nothing needs setting. Otherwise pick one of the two routes below. Either way, set the value in user secrets or in the `ConnectionStrings__Wallet` environment variable. The environment variable wins over user secrets, and both win over the `appsettings` files. User secrets are read only in the Development environment, which is what `dotnet run` uses here. Outside Development there is no default, so the connection string must be supplied.

### Route A: LocalDB on Windows

LocalDB is installed with Visual Studio and with SQL Server Express. Set the connection string:

```powershell
dotnet user-secrets set "ConnectionStrings:Wallet" "Server=(localdb)\MSSQLLocalDB;Database=ZainCashWallet;Trusted_Connection=True;TrustServerCertificate=True" --project src/Wallet.MockApi
```

### Route B: SQL Server in a container

Start SQL Server 2022 in Docker:

```bash
docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=<choose-a-strong-password>" -p 1433:1433 --name zaincash-sql -d mcr.microsoft.com/mssql/server:2022-latest
```

Then set the connection string, with the password you chose, either in user secrets:

```bash
dotnet user-secrets set "ConnectionStrings:Wallet" "Server=localhost,1433;Database=ZainCashWallet;User Id=sa;Password=<the same>;TrustServerCertificate=True" --project src/Wallet.MockApi
```

or in the environment variable (PowerShell shown):

```powershell
$env:ConnectionStrings__Wallet = 'Server=localhost,1433;Database=ZainCashWallet;User Id=sa;Password=<the same>;TrustServerCertificate=True'
```

Never commit a password. Keep it in user secrets or the environment.

### The agent database

The agent (`Agent.Web`, through `Agent.Core`) keeps its pending payments and audit trail in a database of its own, on the same server. Its connection string is named `Agent`; the default, in `src/Agent.Web/appsettings.Development.json`, is the local default instance with the database `ZainCashAgent`:

```text
Server=localhost;Database=ZainCashAgent;Trusted_Connection=True;TrustServerCertificate=True
```

On route A or B, set it the same way as the wallet's, with `--project src/Agent.Web` and the name `Agent` (or the `ConnectionStrings__Agent` environment variable), for example:

```powershell
dotnet user-secrets set "ConnectionStrings:Agent" "Server=(localdb)\MSSQLLocalDB;Database=ZainCashAgent;Trusted_Connection=True;TrustServerCertificate=True" --project src/Agent.Web
```

Create it with `dotnet run --project src/Agent.Web -- migrate`, which applies the agent's migrations, prints `Agent database ready` and exits 0. The agent reaches the wallet at `Agent:WalletBaseUrl` (`http://localhost:5276` in Development) and starts without the wallet running.

## Commands

Run every command from the repository root. The solution file is `ZainCashAgent.slnx`.

| Task | Command |
|---|---|
| Restore the EF tool (once per clone) | `dotnet tool restore` |
| Build | `dotnet build` |
| Test, all projects | `dotnet test` |
| Test, unit project only | `dotnet test tests/Agent.Core.Tests` |
| Test, one class | `dotnet test tests/Agent.Core.Tests -- --filter-class "Agent.Core.Tests.Wallet.HealthTests"` |
| Database tests, required (PowerShell) | `$env:ZAINCASH_TEST_SQL='1'; dotnet test tests/Agent.Core.Tests` |
| Create and seed the database | `dotnet run --project src/Wallet.MockApi -- migrate` |
| Create the agent database | `dotnet run --project src/Agent.Web -- migrate` |
| Add a migration | `dotnet ef migrations add <Name> --project src/Wallet.MockApi` |
| Add an agent migration | `dotnet ef migrations add <Name> --project src/Agent.Core --startup-project src/Agent.Web --output-dir Data/Migrations` |
| Run the mock wallet API | `dotnet run --project src/Wallet.MockApi` |
| Run the web app | `dotnet run --project src/Agent.Web` |
| Run the eval console | `dotnet run --project src/Agent.Eval` |
| Run the screen journeys | `dotnet test tests/Agent.Web.Journeys` |

Notes:

- The tests use xunit.v3 on Microsoft Testing Platform, selected in the root `global.json`, so test filters go after `--` as shown.
- The database tests (collection `wallet-api`, fixture `WalletApiFixture`) probe the test server once, with a 3-second timeout. If it answers, they recreate the test database, apply the migrations and the seed, and run the API in process against it. If it does not, every database test reports Skipped with "SQL Server not reachable at <server>; set ConnectionStrings__WalletTest or start SQL Server"; with `ZAINCASH_TEST_SQL=1` set they fail instead. They never read the `Wallet` connection string. By default they use the local default instance with Windows authentication and the database `ZainCashWallet_Test`. On route A or B, set the `ConnectionStrings__WalletTest` environment variable to a connection string whose database name ends in `_Test`; the fixture refuses any other name, because it drops the database at the start of the run. In PowerShell, `ZAINCASH_TEST_SQL` stays set for the rest of the session; clear it with `Remove-Item Env:ZAINCASH_TEST_SQL`.
- The agent tests (collection `agent`, fixture `AgentHost`) host their own mock wallet on the database `ZainCashWalletAgent_Test` (same server as `ConnectionStrings__WalletTest`), so they run in parallel with the `wallet-api` collection, and recreate the agent's test database: `ConnectionStrings__AgentTest` or, by default, `ZainCashAgent_Test` on the local default instance. Both names must end in `_Test`. They skip and fail under the same rules as the wallet tests.
- `migrate` applies the migrations and the synthetic seed, prints `Database ready: 5 users, 42 accounts, 34 contacts, 86 transactions` and exits 0. It is safe to run twice. The seed comes from the test-set files in `data/seed/*.json`; a missing or invalid file is printed with its name and exits 1. Without a connection string it prints `No connection string 'Wallet'. Set ConnectionStrings:Wallet in appsettings, user secrets or the ConnectionStrings__Wallet environment variable.` and exits 1.
- `seed-hash` (`dotnet run --project src/Wallet.MockApi --no-launch-profile -- seed-hash`) prints the hash of the seed files, the value kept in `data/seed/seed-hash.txt`, and exits 0. It needs no database. The hash is the last line printed (without `--no-launch-profile`, `dotnet run` may print other lines first). See [data/seed/README.md](data/seed/README.md).
- The database tests (`SeedApplyTests.ResetRestoresIdentitiesFromOne`) also check that `SeedReset.ResetAsync` restores the seed with identity values from 1.
- The mock wallet API listens on http://localhost:5276. `GET /health` returns `ok`, and the OpenAPI document is at `/openapi/v1.json` in Development.
- The mock wallet API serves plain HTTP on loopback by design and does not redirect to HTTPS.
- The web app listens on http://localhost:5089. Run order: create both databases (`migrate` for the wallet and for the agent), start the mock wallet, then start the web app; the console reads the wallet at `Agent:WalletBaseUrl` (http://localhost:5276 in Development). Type the scripted sentence in the phone; the console is described in [docs/agent-web.md](docs/agent-web.md).
- Every text the phone shows comes from `src/Agent.Web/Resources/PhoneStrings.resx`, and the scripted parser's sentences from `src/Agent.Core/Parsing/scripted-sentences.json`. Until Khalil writes the Iraqi Arabic, every value is an English placeholder marked `TODO-AR`; the list is in [docs/agent-web.md](docs/agent-web.md).
- The eval console prints a usage line for now. It runs the request sets from milestone 5.
- Install the Playwright browser once, after the first build: `pwsh tests/Agent.Web.Journeys/bin/Debug/net10.0/playwright.ps1 install chromium`.
- The screen journeys (`tests/Agent.Web.Journeys`, collection `journeys`, fixture `JourneyHost`) host the mock wallet and the web console in process on Kestrel, on dynamic loopback ports, and drive them with headless Chromium through the scripted parser (never a live model). The wallet uses `ZainCashWalletJourneys_Test` (override `ConnectionStrings__WalletJourneysTest`) and the console `ZainCashAgent_Journeys_Test` (override `ConnectionStrings__AgentJourneysTest`); both are dropped and recreated at the start of a run, so both names must end in `_Test`. Two consoles run: one with the default card timer and one with a 4-second timer for the timer journey. The journeys skip when SQL Server does not answer the 3-second probe, or when Chromium is not installed (the skip message gives the install command). A run takes about 20 seconds.

## Mock wallet API

The mock wallet is a minimal API on http://localhost:5276 with read endpoints for users, accounts by phone, contacts and aliases, bills, balance, history, the store catalogue and orders; `POST /users/{id}/consent`; `POST /quotes`; `POST /transactions`, the only endpoint that moves money, which requires an `Idempotency-Key` header; `GET /transactions/by-key/{key}` for the status check after a timeout; and the `/admin` routes for the fault switch and the reset. Errors are ProblemDetails with a machine-readable `code`.

The full contract (every route, field, error code, the idempotency, PIN and balance-floor rules, the fault modes, the reset and the admin key) is in [docs/wallet-api.md](docs/wallet-api.md). Ready-made requests against the seed are in `src/Wallet.MockApi/Wallet.MockApi.http`.

Every request except `GET /health` carries the `X-Wallet-Client-Key` header. In Development with no `Wallet:ClientKey` set, any non-empty value is accepted. Outside Development both apps (the wallet and the agent) need `Wallet:ClientKey`, at least 16 characters, in user secrets or the environment: `dotnet user-secrets set "Wallet:ClientKey" "<value>" --project src/Wallet.MockApi` for the wallet, and the same value for the agent with `dotnet user-secrets set "Agent:WalletClientKey" "<value>" --project src/Agent.Web` (or `Agent__WalletClientKey`). In Development with no `Agent:WalletClientKey`, the agent sends `dev`; outside Development it refuses to start without one. Never commit it.

Outside Development the agent needs both `Agent:WalletBaseUrl` and `Agent:WalletClientKey` set (user secrets or the environment); it has no default for either there.

Every `/admin` request also carries the `X-Wallet-Admin-Key` header. In Development with no `Wallet:AdminKey` set, any non-empty value is accepted (the header makes a browser preflight, so a web page cannot reset the wallet). Outside Development, set a key of at least 16 characters in user secrets or the `Wallet__AdminKey` environment variable and send it in that header; never commit it.

Status: milestone 3 of 12 (write path, no model) under final review, see [docs/build-plan.md](docs/build-plan.md).
