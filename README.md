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

If you have that, nothing needs setting. One warning: the default route names the databases `ZainCashWallet` and `ZainCashAgent` (below), so two clones of this repository on the same machine, or on the same server, share them; `migrate` from either clone recreates the seed for both, and a running wallet from one clone serves the other's console. Give a second clone its own database names through user secrets or the environment variables below. Otherwise pick one of the two routes below. Either way, set the value in user secrets or in the `ConnectionStrings__Wallet` environment variable. The environment variable wins over user secrets, and both win over the `appsettings` files. User secrets are read only in the Development environment, which is what `dotnet run` uses here. Outside Development there is no default, so the connection string must be supplied.

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
| Run the eval on the development set | `dotnet run --project src/Agent.Eval -- dev` |
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
- The web app listens on http://localhost:5089. Run order: create both databases (`migrate` for the wallet and for the agent), start the mock wallet, then start the web app; the console reads the wallet at `Agent:WalletBaseUrl` (http://localhost:5276 in Development). Type a development-set message in the phone (the scripted parser is an oracle over `data/requests.dev.jsonl`, below); the console is described in [docs/agent-web.md](docs/agent-web.md).
- Every text the phone shows comes from `src/Agent.Web/Resources/PhoneStrings.resx`, in Khalil's Iraqi Arabic, with no placeholder left (the last two, `Card_TryAgain` and `Card_SendAgain`, were written on 16 Oct 2026). The list is in [docs/agent-web.md](docs/agent-web.md).
- The scripted parser (`Agent:Parser` `scripted`, the default) is an oracle over the development set. It reads `Agent:DevSetPath` (default `data/requests.dev.jsonl`, which must be named exactly that) and the parses in `data/requests.dev.parses.jsonl` next to it, and checks both at load. An exact dev-set message gets that case's parse, and anything else is not understood. The parses are for Khalil's review. See [docs/agent-core.md](docs/agent-core.md), "The parser seam".
- The console's side menu has the settings component (milestone 7): the agent's limits, editable live without a restart (a saved value is in memory only and is gone at restart), the kill switch, and the read-only infrastructure, wallet and model settings. The kill switch is off at every start. While it is on, no card is issued, Confirm and check again move nothing, and the balance question still answers. The Log component lists the latest audit rows (`Agent:LogPageSize`, 100). The fault panel (milestone 8), above the live trace, sets the wallet's fault switch, a model fault and an external debit for the demo, and shows each state as read back. See [docs/agent-web.md](docs/agent-web.md).
- The console has no sign-in: keep it on loopback (as configured). Whoever reaches it can turn the kill switch off or raise the caps, and with the fault panel take money from the demo wallet and set the wallet's process-wide fault switch (in Development the wallet accepts any non-empty admin header). Hosting it anywhere else needs ASP.NET Core authorization on the settings component and the fault panel first, and a real `Wallet:AdminKey`.
- The eval console is described under [Run eval](#run-eval).
- Install the Playwright browser once, after the first build: `pwsh tests/Agent.Web.Journeys/bin/Debug/net10.0/playwright.ps1 install chromium`.
- The screen journeys (`tests/Agent.Web.Journeys`, collection `journeys`, fixture `JourneyHost`) host the mock wallet and the web console in process on Kestrel, on dynamic loopback ports, and drive them with headless Chromium through the scripted parser (never a live model). The wallet uses `ZainCashWalletJourneys_Test` (override `ConnectionStrings__WalletJourneysTest`) and the console `ZainCashAgent_Journeys_Test` (override `ConnectionStrings__AgentJourneysTest`); both are dropped and recreated at the start of a run, so both names must end in `_Test`. Five consoles run: two for U1 (the default card timer, and a 4-second timer for the timer journeys), and one each for U2, U3 and U5 (`Agent:DemoUserId`). The journeys type development-set messages by case id (`DevSet`), and the reset before each one also turns the kill switch off and clears saved settings. The journeys skip when SQL Server does not answer the 3-second probe, or when Chromium is not installed (the skip message gives the install command). The 32 journeys take about two minutes.

## The model (Claude parser)

The parse call is the scripted parser by default (`Agent:Parser` = `scripted`): no model, no key. The Claude parser (milestone 6) calls a candidate of the `Model` section (`haiku`, the default; `sonnet`; `opus`) through `Microsoft.Extensions.AI`. It needs an Anthropic API key, kept in user secrets or the environment, never in a file:

```powershell
dotnet user-secrets set "Anthropic:ApiKey" "<your key>" --project src/Agent.Web
dotnet user-secrets set "Agent:Parser" "claude" --project src/Agent.Web
```

The first command stores the key for both the console and the runner (`Agent.Eval` reads only `Anthropic:ApiKey` from the console's store). The second switches the console to the model; remove it (`dotnet user-secrets remove "Agent:Parser" --project src/Agent.Web`) to go back to the scripted parser. `ANTHROPIC_API_KEY` works instead of the first command. With `Agent:Parser` = `claude` and no key, the console and the runner refuse to start with a message naming both settings (never a value). Another candidate: `dotnet user-secrets set "Model:Candidate" "sonnet" --project src/Agent.Web`. The screen journeys always use the scripted parser.

A baseline run on the development set (wallet running): `dotnet run --project src/Agent.Eval -- dev --parser claude:haiku` (or `sonnet`, `opus`). It prints the estimated cost first and the real cost at the end. Expect cents on Haiku and about one to three USD per development run on Sonnet or Opus (72 cases; questions and taps make several calls per case, and thinking is billed as output). How the parse call works, its settings and its faults: [docs/agent-model.md](docs/agent-model.md).

The live model test is skipped unless asked for: `$env:ZAINCASH_MODEL_TESTS='1'; dotnet test tests/Agent.Core.Tests -- --filter-class "Agent.Core.Tests.Parsing.LiveModelTests"` (clear it afterwards with `Remove-Item Env:ZAINCASH_MODEL_TESTS`). It sends each candidate three prompt examples and the development set's clean balance questions, and prints the tokens and the cost of each call.

## Run eval

`Agent.Eval` plays a request set through the same conversation logic as the phone (the scripted parser by default, or a model candidate with `--parser claude:<candidate>`), grades every case against its answer key and writes a report. How it plays and grades each case, the red-team pass mark and the first development run are in [docs/agent-eval.md](docs/agent-eval.md).

1. Create the wallet database once (`dotnet run --project src/Wallet.MockApi -- migrate`) and start the wallet: `dotnet run --project src/Wallet.MockApi`.
2. Run the development set: `dotnet run --project src/Agent.Eval -- dev`.

Options: `--only CLN-01,CLN-02` runs some cases; `--wallet http://localhost:5276` points at another wallet; `--out <dir>` writes the report elsewhere; `--parser claude:haiku` (or `sonnet`, `opus`) measures a model candidate, printing the estimated cost before the run and the real cost after it (it needs the key, see The model above). The settings are in `src/Agent.Eval/appsettings.json` (sections `Eval` and `Agent`); the runner uses its own agent database, `ZainCashAgent_Eval` (`ConnectionStrings:Agent`), which it creates and migrates, because before every case it resets the wallet to the seed, closes open pending records and empties the audit log. It runs in the Development environment unless `DOTNET_ENVIRONMENT` says otherwise.

The console prints one line per case (id, passed or the first failing field) and one per category, then the report paths: `docs/reports/dev-<parser>-<UTC date>-<UTC HHmm>.md` and `.json` (for example `dev-claude-haiku-2026-10-14-2130`). A report holds case ids, categories, codes, figures and verdicts, never a case's text. Exit codes: 0 the run completed (whatever the pass rate), 2 the command line is wrong or the held-out guard refused, 1 the wallet or the agent database does not answer, or the set file is missing.

The held-out set (`data/requests.heldout.jsonl`) is sealed (rule 7). `dotnet run --project src/Agent.Eval -- heldout` refuses and prints the rule without opening any file. Only the milestone 11 run uses `heldout --milestone-11-run`, which hashes the file and refuses unless the SHA-256 equals the one recorded in `docs/disclosure.md`.

## Mock wallet API

The mock wallet is a minimal API on http://localhost:5276 with read endpoints for users, accounts by phone, contacts and aliases, bills, balance, history, the store catalogue and orders; `POST /users/{id}/consent`; `POST /quotes`; `POST /transactions`, the only endpoint that moves money, which requires an `Idempotency-Key` header; `GET /transactions/by-key/{key}` for the status check after a timeout; and the `/admin` routes for the fault switch and the reset. Errors are ProblemDetails with a machine-readable `code`.

The full contract (every route, field, error code, the idempotency, PIN and balance-floor rules, the fault modes, the reset and the admin key) is in [docs/wallet-api.md](docs/wallet-api.md). Ready-made requests against the seed are in `src/Wallet.MockApi/Wallet.MockApi.http`.

Every request except `GET /health` carries the `X-Wallet-Client-Key` header. In Development with no `Wallet:ClientKey` set, any non-empty value is accepted. Outside Development both apps (the wallet and the agent) need `Wallet:ClientKey`, at least 16 characters, in user secrets or the environment: `dotnet user-secrets set "Wallet:ClientKey" "<value>" --project src/Wallet.MockApi` for the wallet, and the same value for the agent with `dotnet user-secrets set "Agent:WalletClientKey" "<value>" --project src/Agent.Web` (or `Agent__WalletClientKey`). In Development with no `Agent:WalletClientKey`, the agent sends `dev`; outside Development it refuses to start without one. Never commit it.

The console's fault panel (milestone 8) also sends `X-Wallet-Admin-Key`, from `Console:WalletAdminKey` (default `dev`, which the wallet accepts in Development when it has no `Wallet:AdminKey`). When the wallet has an admin key, set the same value for the console: `dotnet user-secrets set "Console:WalletAdminKey" "<value>" --project src/Agent.Web` (or `Console__WalletAdminKey`). Outside Development the console refuses to start unless `Console:WalletAdminKey` is set, is not the placeholder `dev`, and has at least 16 characters (the wallet's own minimum). The settings component shows it as `set`, never its value.

Outside Development the agent needs both `Agent:WalletBaseUrl` and `Agent:WalletClientKey` set (user secrets or the environment); it has no default for either there.

Every `/admin` request also carries the `X-Wallet-Admin-Key` header. In Development with no `Wallet:AdminKey` set, any non-empty value is accepted (the header makes a browser preflight, so a web page cannot reset the wallet). Outside Development, set a key of at least 16 characters in user secrets or the `Wallet__AdminKey` environment variable and send it in that header; never commit it.

Status: milestone 8 of 12 (bills and faults on screen) done and tagged `milestone-8` on 16 Oct 2026: the bill flow (D9) with the bill-or-store rule (schema version 2, prompt version 3), part of a bill refused with the full amount, the fault panel beside the live trace (the six faults of slide 2), the Checking state on the card with the lookup by key and an automatic settle under the same key, `POST /admin/debit` in the mock wallet, and 32 screen journeys. On the development set (77 cases, the oracle 68) Sonnet and Opus pass 67 of 77 on the median of three clean runs and Haiku 62; the nine failures common to all are the items deferred to milestone 9; 0 executions without a tap, 0 double payments and 0 masking failures on every run; the reports are in `docs/reports/`. The slide 2 targets are in [docs/targets.md](docs/targets.md) and every runner report prints pass or miss against them. The status tables of milestones 4 to 8 are in [docs/build-plan.md](docs/build-plan.md); the six failure modes open [docs/failure-modes.md](docs/failure-modes.md). Next: milestone 9, the extras (aliases, purchases, compound requests).

## Disclosure

Every model, provider, library, data source and AI coding assistant used in this entry, in short. The full list, one line per item and added in the same commit as the thing it names, is [docs/disclosure.md](docs/disclosure.md).

- AI coding assistant: Claude Code (Anthropic), with the Claude Fable 5.1, Opus and Haiku models, for planning, implementation and review. All Iraqi Arabic wording, answer keys and matching rules are written by Khalil.
- Models and provider: Claude Haiku 5.5 (`claude-haiku-5-5`, the default candidate), Claude Sonnet 5.5 (`claude-sonnet-5-5`) and Claude Opus 5.5 (`claude-opus-5-5`), all through the Anthropic API, each used for one read-only structured parse call per message. The key is read from user secrets (`Anthropic:ApiKey`) or `ANTHROPIC_API_KEY` and is never committed.
- Runtime and libraries: .NET 10 SDK and ASP.NET Core 10; Anthropic 12.56.0 (official C# SDK) and Microsoft.Extensions.AI 10.11.0 (the `IChatClient` abstraction); MudBlazor 9.11.0; Microsoft.EntityFrameworkCore.SqlServer and .Design 10.0.13 with the dotnet-ef 10.0.13 tool; Microsoft.AspNetCore.OpenApi 10.0.13; Microsoft.Extensions.Http, Hosting, Options and Configuration 10.0.13; System.Security.Cryptography.Pkcs 10.0.13; SQL Server (local instance, LocalDB or container).
- Tests: xunit.v3 4.0.2 on Microsoft Testing Platform, xunit.runner.visualstudio 4.0.1, Microsoft.NET.Test.Sdk 18.11.0, Microsoft.AspNetCore.Mvc.Testing 10.0.13, Microsoft.Playwright 1.63.0 with Chrome Headless Shell 153.
- Data: the test-set workbook v0.15 (Khalil, 14 Oct 2026), exported as `data/seed/*.json`, `data/requests.dev.jsonl`, `data/requests.heldout.jsonl` and `docs/matching-rules.md`. All synthetic. The held-out file is sealed ([docs/HELDOUT_SEAL.md](docs/HELDOUT_SEAL.md)).
- External resources: Roboto from Google Fonts (MudBlazor's default font; falls back to system fonts offline).
