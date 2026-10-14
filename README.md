# ZainCash Bill Pay and Transfer Agent

An agent that turns one sentence in Iraqi Arabic into one confirmation card and one tap, against a mock wallet API, for transfers, bills, store purchases and the read-only balance question. It is an entry for the ZainCash Agentic AI Challenge Iraq, topic 04, and its design is in [docs/architecture.md](docs/architecture.md).

Rules: the nine rules that are never broken are listed in [CLAUDE.md](CLAUDE.md).

## Repository layout

- `src/`: the four applications: `Wallet.MockApi` (minimal API, EF Core with SQL Server), `Agent.Core` (orchestrator and request pipeline), `Agent.Web` (Blazor Server with MudBlazor), `Agent.Eval` (console that runs the request sets).
- `tests/`: `Agent.Core.Tests` (xunit.v3 unit and API tests) and `Agent.Web.Journeys` (Playwright screen journeys).
- `data/`: the synthetic seed specification and the request sets.
- `docs/`: architecture, build plan, failure modes and disclosure.

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

## Commands

Run every command from the repository root. The solution file is `ZainCashAgent.slnx`.

| Task | Command |
|---|---|
| Restore the EF tool (once per clone) | `dotnet tool restore` |
| Build | `dotnet build` |
| Test, all projects | `dotnet test` |
| Test, unit project only | `dotnet test tests/Agent.Core.Tests` |
| Test, one class | `dotnet test tests/Agent.Core.Tests -- --filter-class "Agent.Core.Tests.Wallet.HealthTests"` |
| Database test (opt-in, PowerShell) | `$env:ZAINCASH_TEST_SQL='1'; dotnet test tests/Agent.Core.Tests` |
| Create and seed the database | `dotnet run --project src/Wallet.MockApi -- migrate` |
| Add a migration | `dotnet ef migrations add <Name> --project src/Wallet.MockApi` |
| Run the mock wallet API | `dotnet run --project src/Wallet.MockApi` |
| Run the web app | `dotnet run --project src/Agent.Web` |
| Run the eval console | `dotnet run --project src/Agent.Eval` |
| Run the screen journeys | `dotnet test tests/Agent.Web.Journeys` |

Notes:

- The tests use xunit.v3 on Microsoft Testing Platform, selected in the root `global.json`, so test filters go after `--` as shown.
- The database test reports Skipped unless `ZAINCASH_TEST_SQL=1` is set. It does not read the `Wallet` connection string. By default it uses the local default instance with Windows authentication and the database `ZainCashWallet_Test`. On route A or B, set the `ConnectionStrings__WalletTest` environment variable to a connection string whose database name ends in `_Test`; the test refuses any other name, because it drops the database before and after it runs. In PowerShell, `ZAINCASH_TEST_SQL` stays set for the rest of the session; clear it with `Remove-Item Env:ZAINCASH_TEST_SQL`.
- `migrate` applies the migrations and the synthetic seed, prints `Database ready: 3 users, 3 accounts` and exits 0. It is safe to run twice. Without a connection string it prints `No connection string 'Wallet'. Set ConnectionStrings:Wallet in appsettings, user secrets or the ConnectionStrings__Wallet environment variable.` and exits 1.
- The mock wallet API listens on http://localhost:5276. `GET /health` returns `ok`, and the OpenAPI document is at `/openapi/v1.json` in Development.
- The mock wallet API serves plain HTTP on loopback by design and does not redirect to HTTPS.
- The web app listens on http://localhost:5089.
- The eval console prints a usage line for now. It runs the request sets from milestone 5.
- Install the Playwright browsers once, after the first build: `pwsh tests/Agent.Web.Journeys/bin/Debug/net10.0/playwright.ps1 install`.

Status: milestone 1 of 12, see [docs/build-plan.md](docs/build-plan.md).
