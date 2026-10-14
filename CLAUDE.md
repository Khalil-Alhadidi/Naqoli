# ZainCash Agentic AI Challenge Iraq · Topic 04 · Bill Pay & Transfer Agent

An agent that turns one sentence in Iraqi Arabic ("send fifty thousand to my brother Hammoudi") into one confirmation card and one tap, against a mock wallet API. Four request types: transfer, bill, store purchase, and the balance question, which is read only and has no card and no tap. Hackathon entry by Khalil Ahmed Ibrahim.

## Read before you write code

- `docs/architecture.md` is the agreed design. Decisions D1 to D22 are settled. Do not reopen or work around them. If one seems wrong or blocks you, stop and ask Khalil.
- `docs/build-plan.md` is the order of work. Do one milestone at a time. A milestone is finished only when every "Done when" line is true and shown by a test or a committed file.

Read the section you need from those files when you start a task. Do not rely on this summary for detail.

## Rules that are never broken

1. **No tap, no money.** Only the Confirm handler, given one pending ID, can reach the wallet's execute call. Words never execute. Keep the test that proves this passing.
2. **The model is read only.** It has no tools that write. It makes one parse call per message and returns a strict structure. Code validates that structure and treats it as a request to check, not a command.
3. **The card is built by code.** No field on the confirmation card, and no figure in any reply, comes from model text.
4. **Replies are templates.** Everything the agent says comes from the phone resource file. Never show generated text to the user.
5. **Masking.** Phone and account numbers become tokens before text reaches the model. The model sees the user's own words and a one-line note of what is pending, and nothing fetched from the wallet: no balances, no stored contact names or aliases, no purchase codes, no PINs.
6. **Idempotency.** Every execute carries a key. A pending record leaves Pending only once. Unknown status is "checking", never "failed", and a retry reuses the same key.
7. **The held-out set is sealed.** After it is committed in milestone 5, do not open, print, search or summarise `data/requests.heldout.jsonl`. Only `Agent.Eval` reads it, once, in milestone 11. Tune on `data/requests.dev.jsonl` only. The test-set workbook (`zaincash_testset_v0.10.xlsx`) holds the same held-out cases, so do not open it either. Its matching and question rules reach you as `docs/matching-rules.md`.
8. **Synthetic data only.** No real names, numbers, documents or transactions anywhere in the repository.
9. **No secrets in the repository.** Model keys and connection strings go in user secrets or environment variables.

If a task seems to need breaking one of these, stop and ask.

## Things only Khalil writes

- **Iraqi Arabic wording.** Phone strings and test requests are written by Khalil. When a new string is needed, add the key to the resource file with an English placeholder marked `TODO-AR` and list it in your summary. Do not write or "improve" the Arabic.
- **Answer keys, targets and matching rules.** Do not change an answer key, a target or `docs/matching-rules.md` to make a test pass. Report the mismatch.
- **Scope.** Do not add features, request types or settings that the architecture does not name. Suggest them instead.

## Stack

- .NET 10. Blazor Server with MudBlazor 9. SQL Server through EF Core, created and seeded by migrations.
- Model access through `Microsoft.Extensions.AI`, so a candidate model is a configuration change.
- Playwright for .NET for screen journeys. Journeys use the scripted parser, never a live model.
- Projects: `Wallet.MockApi`, `Agent.Core`, `Agent.Web`, `Agent.Eval`, `Agent.Core.Tests`, `Agent.Web.Journeys`. The agent talks to the mock wallet over HTTP only.
- Anything not named here or in the architecture (test framework, logging library, and so on) is Khalil's choice. Ask once, then record the answer in this file.
- Chosen by Khalil on 14 Oct 2026: tests use xUnit; logging uses the built-in Microsoft.Extensions.Logging with no third-party sink.

## Language and layout

- Inside the phone frame: Iraqi Arabic, right to left, Arabic-Indic digits. Typed input is accepted in either digit form.
- Everything else on the page (grids, settings, log, learning data), plus code, comments, commit messages and docs: English, left to right.
- Page layout B: side menu, selected component in the middle, live trace always visible, phone frame on the right.
- No Arabic in C#. The seed's Arabic values live in `data/seed/*.json` (Khalil's test-set export; never edited by Claude); tests take seed text from the loaded `SeedSet` through `SeedFacts` (`Contact("S01").SavedName`), never as a literal.

## Working habits

- Small commits with clear messages. The organisers check that the commit history spans the build period.
- When you add a library, tool, model or data source, add a line to `docs/disclosure.md` in the same commit. Claude Code itself is listed there.
- When you find a new way the system can fail, add it to `docs/failure-modes.md` with how it is handled.
- Limits, thresholds and timers are configuration, shown in the settings component. Do not hard-code them.
- At the end of a task, say which "Done when" lines are now true, which are not, and what you were unsure about.

## Commands

Run from the repository root. Solution: `ZainCashAgent.slnx`. The two database routes (LocalDB, container) are in `README.md`.

- Restore tools (once per clone): `dotnet tool restore`
- Build: `dotnet build`
- Test: `dotnet test` (all) or `dotnet test tests/Agent.Core.Tests`; one class: `dotnet test tests/Agent.Core.Tests -- --filter-class "<Namespace.Class>"`
- Database tests (collection `wallet-api`; run with `dotnet test` when the test server answers, Skipped otherwise; do not read the `Wallet` string; default is the local default instance, database `ZainCashWallet_Test`; on LocalDB or a container set `ConnectionStrings__WalletTest` to a string whose database name ends in `_Test`). To make an unreachable server fail instead of skip: `$env:ZAINCASH_TEST_SQL='1'; dotnet test tests/Agent.Core.Tests` (clear afterwards with `Remove-Item Env:ZAINCASH_TEST_SQL`)
- Create and seed: `dotnet run --project src/Wallet.MockApi -- migrate` (prints `Database ready: 5 users, 42 accounts, 34 contacts, 86 transactions`; the seed is `data/seed/*.json`, loaded and validated at start, history dated relative to today in Iraq time)
- Seed hash: `dotnet run --project src/Wallet.MockApi --no-launch-profile -- seed-hash` (prints the SHA-256 of the eight seed files, the value in `data/seed/seed-hash.txt`; the hash is the last line of the output; no database needed; a changed seed file fails `SeedHashTests` until the file is re-frozen and every answer key re-checked)
- The default `Wallet` connection string is in `src/Wallet.MockApi/appsettings.Development.json` and applies only in Development (what `dotnet run` uses).
- Add a migration: `dotnet ef migrations add <Name> --project src/Wallet.MockApi`
- Agent database (milestone 3): create or update with `dotnet run --project src/Agent.Web -- migrate` (prints `Agent database ready`; connection string `ConnectionStrings:Agent`, default in `src/Agent.Web/appsettings.Development.json`, database `ZainCashAgent`); add a migration with `dotnet ef migrations add <Name> --project src/Agent.Core --startup-project src/Agent.Web --output-dir Data/Migrations`. Agent tests (collection `agent`, fixture `AgentHost`) use `ConnectionStrings__AgentTest` (default database `ZainCashAgent_Test`) and host their own wallet on `ZainCashWalletAgent_Test`; both names must end `_Test`.
- Wallet client key: every wallet route except `/health` needs `X-Wallet-Client-Key`; in Development with no key any non-empty value works; otherwise set `Wallet:ClientKey` (wallet) and `Agent:WalletClientKey` (agent) in user secrets, 16+ characters.
- Run API: `dotnet run --project src/Wallet.MockApi` (http://localhost:5276, `/health`)
- Reset the wallet to the seed (API running): `Invoke-RestMethod -Method Post http://localhost:5276/admin/reset -Headers @{ 'X-Wallet-Admin-Key' = 'dev'; 'X-Wallet-Client-Key' = 'dev' }` (returns the row counts; in Development with no `Wallet:AdminKey` any non-empty value works, otherwise send the key, at least 16 characters)
- The mock wallet contract (routes, fields, error codes, faults, admin key) is `docs/wallet-api.md`; sample requests are in `src/Wallet.MockApi/Wallet.MockApi.http`.
- Run web: `dotnet run --project src/Agent.Web` (http://localhost:5089)
- Run eval (wallet running): `dotnet run --project src/Agent.Eval -- dev` (plays `data/requests.dev.jsonl` through the conversation, grades every case, writes `docs/reports/dev-<parser>-<date>-<HHmm>.md` and `.json`; options `--only id,id`, `--wallet url`, `--out dir`; its own agent database `ZainCashAgent_Eval`). The held-out set runs only as `Agent.Eval heldout --milestone-11-run`, once, in milestone 11; without the flag the runner refuses and opens nothing (rule 7). Runner docs: `docs/agent-eval.md`.
- Model parser (milestone 6, `docs/agent-model.md`): `Agent:Parser` is `scripted` (default, no key) or `claude`. Key, once for console and runner: `dotnet user-secrets set "Anthropic:ApiKey" "<key>" --project src/Agent.Web` (or `ANTHROPIC_API_KEY`); never in a file or a log. Console on the model: `dotnet user-secrets set "Agent:Parser" "claude" --project src/Agent.Web` (candidate `Model:Candidate`: `haiku` default, `sonnet`, `opus`). Eval on a model: `dotnet run --project src/Agent.Eval -- dev --parser claude:<candidate>` (prints the estimated and the real cost; reports `dev-claude-<candidate>-<date>-<HHmm>`). Live model test, gated: `$env:ZAINCASH_MODEL_TESTS='1'; dotnet test tests/Agent.Core.Tests -- --filter-class "Agent.Core.Tests.Parsing.LiveModelTests"` (clear with `Remove-Item Env:ZAINCASH_MODEL_TESTS`). The prompt `src/Agent.Core/Parsing/parse-prompt.md` is frozen at version 1 for the baselines.
- Run journeys: `dotnet test tests/Agent.Web.Journeys` (Chromium once: `pwsh tests/Agent.Web.Journeys/bin/Debug/net10.0/playwright.ps1 install chromium`; `pwsh` is PowerShell 7 or later). The journeys host the wallet and the console on Kestrel with their own databases `ZainCashWalletJourneys_Test` and `ZainCashAgent_Journeys_Test`, reset the wallet before each case, and skip when SQL Server or Chromium is missing. Run the console by hand: wallet first (`dotnet run --project src/Wallet.MockApi`), then `dotnet run --project src/Agent.Web`; the phone strings are `TODO-AR` placeholders until Khalil writes them (`src/Agent.Web/Resources/PhoneStrings.resx`), and the scripted parser's sentences are in `src/Agent.Core/Parsing/scripted-sentences.json`.
