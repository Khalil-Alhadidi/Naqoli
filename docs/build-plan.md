# Topic 04 · Bill Pay & Transfer Agent · Build plan

**Version 0.4, proposed on 05/10/2026. The order of milestones is not yet confirmed by Khalil.** It follows `docs/architecture.md` version 0.11. Decision numbers (D1 to D22) refer to that file.

No dates yet. The organisers' guidelines for the build round have not arrived, so milestones are ordered, not scheduled.

## How to use this plan

- Work on one milestone at a time, in the order below.
- A milestone is finished only when every line under "Done when" is true and has a test or a committed file that shows it.
- Do not start the next milestone until the current one is finished, unless Khalil says so.
- If a milestone needs something this plan or the architecture does not settle, stop and ask. Do not decide it in code.
- Commit in small steps with clear messages. The organisers check that the commit history spans the build period.

## Why this order

1. **Write path before the model.** Steps 4 to 9 of the pipeline are plain code. They are built and proven with a hand-written request before any model is called.
2. **Held-out set before any prompt.** The held-out requests are committed before the first real prompt exists, so the commit history proves they were never tuned on.
3. **Cut list last.** Anything on the cut list is built in reverse cut order, so dropping it never touches what came before.

## Milestones at a glance

| # | Milestone | Needs | On the cut list |
|---|---|---|---|
| 1 | Skeleton | nothing | no |
| 2 | Mock wallet and seed data | 1 | no |
| 3 | Write path, no model | 2, the matching rules | no |
| 4 | First slice on screen | 3 | no |
| 5 | Test set and runner (the gate) | 2, 3, Khalil's wording | no |
| 6 | Model in | 5 | no |
| 7 | Transfers and the balance question, complete | 6 | no |
| 8 | Bills and faults on screen | 7 | no |
| 9 | Extras: aliases, purchases, compound | 8 | yes, all three |
| 10 | Model choice and freeze | 9, or 8 if 9 is cut | third candidate only |
| 11 | Held-out run, once | 10 | no |
| 12 | Demo readiness | 11 | no |

## Milestone 1: Skeleton

**Delivers**

- The solution laid out as in section 6 of the architecture: `Wallet.MockApi`, `Agent.Core`, `Agent.Web`, `Agent.Eval`, `Agent.Core.Tests`, `Agent.Web.Journeys`.
- EF Core with SQL Server, the first migration, and a seed command.
- A README with two routes to a database: LocalDB on Windows, or SQL Server in a container. The connection string is configuration.
- `docs/disclosure.md` and `docs/failure-modes.md` started, even if short.

**Done when**

- A fresh clone builds and the tests pass.
- One command creates and seeds the database.
- Both apps start. They do not need to do anything useful yet.
- No key or secret is in the repository.

## Milestone 2: Mock wallet and seed data

**Delivers**

- Every endpoint in section 7 of the architecture.
- The fault switch (timeout, error, slow response) and reset.
- Synthetic seed data: several users, each with one default account (D10), contacts with planted duplicate and similar names, seeded aliases, biller accounts with amounts due, the store catalogue (D11), and three months of history.

**Done when**

- API tests show that the same idempotency key sent twice executes once.
- A timeout injected after execute leaves a transaction that `GET /transactions/by-key/{key}` finds.
- Reset restores the seed exactly.
- A balance can never go below zero.
- The seed is frozen and tagged. The answer keys in milestone 5 depend on it, so any later change to the seed means checking every answer key again.

Note, 14 Oct 2026: the seed was replaced by Khalil's test-set export (commit 462187f) in milestone 2b and re-frozen and tagged `seed-v2` after the milestone 2b review; the milestone 2 'Done when' lines were re-run on it.

## Milestone 3: Write path, no model

**Delivers**

- In `Agent.Core`: resolver, quote, checks, pending store, the state machine from section 5 of the architecture, executor, and audit log.
- All of it driven by a structured request built in tests. No model call exists yet.
- Seeded aliases resolve by exact match after Arabic normalising. Creating and managing aliases comes in milestone 9.
- Matching follows the Rules and Matching sheets of the test-set workbook (D14). These are needed before the resolver is written.

**Done when**

- A double confirm on one pending record executes once.
- An expired or cancelled record cannot execute.
- A retry from Checking reuses the same key.
- The recheck at execute catches a balance that changed after the card was built.
- Every state change writes an audit row.
- A test fails if anything other than the Confirm handler can reach the wallet's execute call. This is the "no tap, no money" rule as a test.

Note, 14 Oct 2026: all six lines are shown by tests in `tests/Agent.Core.Tests`. Double confirm: `ConfirmHandlerTests.DoubleTapExecutesOnce` and `PendingStoreTests.ConcurrentBeginExecuteWinsOnce`. Expired or cancelled: `ConfirmHandlerTests.ExpiredCannotExecute` and `CancelledCannotExecute`. Retry from Checking: `ConfirmHandlerTests.RetryReusesTheSameKey`. Recheck: `ConfirmHandlerTests.BalanceChangedAfterTheCard`. Audit row per state change: `PendingStoreTests.EveryTransitionWritesAnAuditRow`. No tap, no money: `NoTapNoMoneyTests.OnlyTheConfirmHandlerCanReachExecute` and `ExecuteIsInternal`. Beyond the list, bill and product resolution (`BillResolver`, `ProductResolver`) were built at code level, because the checks need them; their screens stay in milestones 8 and 9b. The provisional codes `amount_below_minimum`, `pin_locked` and `caller_cancelled`, and the warning key `repeat`, await Khalil. The write path is described in [agent-core.md](agent-core.md).

## Milestone 4: First slice on screen

**Delivers**

- The layout B shell (D17): side menu, the selected component in the middle, the live trace, and the phone frame on the right.
- Inside the phone: the chat, the card built by code, Confirm with a timer, and Cancel.
- A scripted parser behind the parser interface. It knows a few fixed sentences and nothing else. It stays in the codebase afterwards so the screen journeys never depend on a live model.
- The wallet data component, read only.

**Done when**

- The Hammoudi request from slide 1, typed in the phone, shows the card, and Confirm moves the money in the mock wallet.
- The wallet data component shows the new balance and the new history row.
- The live trace shows each step with its zone (User, Code, Model, Write).
- Three Playwright journeys pass: the happy path, the timer ending and killing the button, and a double tap.
- The phone frame is right to left and the console is left to right.

## Milestone 5: Test set and runner (the gate)

**Delivers**

- `data/requests.dev.jsonl` and `data/requests.heldout.jsonl`, 62 requests each (124 in all), with answer keys, in the category mix of D14. They are exported from `zaincash_testset_v0.10.xlsx`. Claude drafts each case and its key; Khalil rewrites the wording.
- `Agent.Eval`: runs a set through the same orchestrator as the chat, simulates the tap, grades card fields and final wallet state against the answer key, and writes a report with counts, percentages and ranges, split by category.
- On every case the runner also does three things (D12): it checks that the text sent to the model has no phone number, no account number and no balance; it records the time and the cost; and it checks the learning-log entry where the answer key expects one.

**Done when**

- The held-out file is committed, and its commit hash is written in `docs/disclosure.md`.
- At that commit, the repository contains no model prompt other than the scripted parser.
- The grader has its own tests, including a known wrong card being marked wrong and a missing tap being marked as no execution.
- The three per-case checks have their own tests: a planted phone number in the outbound text fails the case, a planted balance fails the case, and a missing learning-log entry fails the case when the key expects one.
- A balance case is graded as: no card, no execution, and the reply figure equal to the wallet balance.
- The runner completes on the development set with the scripted parser. Low scores are expected here; the point is that the grader works.

**After this milestone, the held-out file is not opened, printed or used by anyone, including Claude Code, until milestone 11.**

**Held-out commit:** `462187f33dd18d08b8176ee767a5114ba9a7f1b2` (14 Oct 2026, "Add test data: seed, matching rules, dev set, held-out set (sealed)"). It adds `data/requests.heldout.jsonl` (62 cases, SHA-256 `3e396362fd6cea5fa81e26ad9e4c17e9c4de2066ee186e963690a14c195ca1c1`) together with the development set, the seed files under `data/seed/` and `docs/matching-rules.md`, all exported from the test-set workbook v0.15. The file was copied as bytes and never opened. Any later commit that touches it, other than the milestone 11 run, must be explained in `docs/disclosure.md`.

## Milestone 6: Model in

**Delivers**

- The masker: phone and account numbers become tokens before the text leaves the process.
- The parse call through `Microsoft.Extensions.AI`, returning a list of requests with intent and slots (D1, D7).
- The schema validator.
- Question templates for a missing or unclear slot.
- The learning log for anything unresolved, with a reason.
- Switching between model candidates by configuration.

**Done when**

- A baseline report on the development set is committed for every candidate, before any prompt tuning. It includes time and cost per case.
- The model recognises the balance question as its own intent (D22). The reply itself comes in milestone 7, with the consent screen.
- A test inspects the outbound payload and finds no phone number or account number, and nothing fetched from the wallet: no balance, no stored contact name or alias. The user's own words are the only names in it.
- Invalid or unexpected model output becomes a question to the user. It never becomes an exception or a card.
- A model outage or timeout shows the handover message.
- The real parser is used in the chat. The scripted parser is used in the screen journeys.

## Milestone 7: Transfers and the balance question, complete

**Delivers**

- Name matching with Arabic normalising, and options to tap when more than one contact matches (D3).
- A typed phone number, shown in full with its registered name and the "first time" line (D16).
- No account-number entry and no QR scanning (D21). A typed account number is still masked, gets no card, and the reply asks for a phone number or a saved name.
- Refusals from templates: insufficient balance, unknown contact, over a cap. No comment on the user's finances.
- The balance question (D22): code reads the user's own balance and fills a template. No card, no tap, and only after consent.
- Typing while a card waits, all four cases (D6).
- Try again and Send again (D19).
- Caps and the PIN field, read from settings (D8).
- The first-use consent screen.
- The kill switch.
- The settings component and the log component in the console.

**Done when**

- The transfer cases in the development set meet the targets from slide 2.
- The balance cases in the development set pass, and the outbound-text check finds no balance on any case.
- Before consent, a balance question shows the consent screen and no figure.
- Each behaviour above has a screen journey, with the scripted parser.
- A changed limit in settings takes effect without a restart.
- The PIN never appears in the chat, the audit log or the learning log.

## Milestone 8: Bills and faults on screen

**Delivers**

- The bill flow (D9): the amount comes from the biller and is paid in full, two accounts with the same biller become options, and nothing due means no card.
- A request to pay part of a bill gets no card. The reply says bills are paid in full and gives the full amount, taken from the biller by code.
- The fault switch in settings, always at hand.
- The Checking state on screen, with the lookup by key.
- The duplicate warning from history on a repeated request.

**Done when**

- The bill cases in the development set meet the targets.
- The fault-injection cases give zero double payments.
- A timeout shows "checking", never "failed", and resolves to done or not done by key.
- `docs/failure-modes.md` describes all six failure modes from slide 2, each with how it is handled and which test shows it.

## Milestone 9: Extras, in reverse cut order

Three separate pieces. Build them in this order. Each one can be dropped without changing the others.

**9a. Alias saving and managing (D15, D16)**

- Delivers: the offer to save an alias after a completed payment, saving only with approval, the directory to manage them, and options for a near match.
- Done when: the alias cases in the development set pass, an alias always points to exactly one account, and alias text never reaches the model.

**9b. Store purchases (D11)**

- Delivers: the purchase flow against the catalogue, the code shown with a copy button, and My orders.
- Done when: the purchase cases in the development set pass, the code never reaches the model, and logs keep only the last digits of a code.

**9c. Compound requests (D7)**

- Delivers: separate cards in the order spoken, each with its own tap, the next card built only after the previous one is finished, and a cap of three.
- Done when: the compound cases in the development set pass, and a cancel, expiry or failure on one card still leads to the next.

**If a piece is cut:** the agent gives a safe reply and no card for those requests, the piece is listed under known gaps, and its held-out cases are reported as unsupported. They are not removed from the held-out set.

## Milestone 10: Model choice and freeze

**Delivers**

- The candidates compared on the development set with the final prompt, under the rule in D13: fewest wrong cards wins, and a close call goes to the cheaper or in-country model.
- The learning data component in the console, with its count in the menu.

**Done when**

- The comparison report is committed.
- The chosen model, the prompt and the seed are tagged in the repository.
- After the tag, only fixes to plain code bugs are allowed, and each one is noted in `docs/disclosure.md`.

## Milestone 11: Held-out run, once

**Delivers**

- One run of the held-out set on the chosen model.
- The results report: every target from slide 2 with its range, the split by category, median time from message to card, and cost per completed request.

**Done when**

- The report is committed as produced, misses included.
- Nothing in the prompt or the code is changed in response to the held-out results. If something must change, the change and the reason are written in `docs/disclosure.md`.

## Milestone 12: Demo readiness

**Delivers**

- The finished console in layout B.
- `docs/disclosure.md` complete: every model, tool, library, data source and AI coding assistant.
- A script for the first minute of the demo.
- A rehearsal where someone other than Khalil supplies the input.

**Done when**

- The demo runs from a fresh clone on a second machine, following only the README.
- The Playwright suite has 10 to 15 journeys and passes.
- The rehearsal has been done once and its surprises are fixed or written under known gaps.

## Stretch: voice

Only after milestone 12, and only if the core meets its targets. Speech becomes text before step 1 of the pipeline, and nothing after that changes. It is reported at its measured level.

## Khalil's track, alongside the code

| Item | Why it matters | Needed by |
|---|---|---|
| Rewrite the 124 requests in Iraqi dialect | Only Khalil can do the wording, and it is the slowest item | Milestone 5 |
| Export the Rules and Matching sheets as `docs/matching-rules.md` | The resolver is built from them, and the workbook itself stays out of the coding sessions because it holds the held-out cases | Milestone 3 |
| Write the phone strings in the resource file (D20) | Everything the agent says is a template written by an Iraqi | Milestone 4 for the first few, milestone 7 for the rest |
| Two or three short conversations with counter staff or their customers, written up | "Evidence you spoke to someone" is part of a 20% criterion | Milestone 12 |
| Name the three model candidates and get access to each | The decisions describe their types but not their names | Milestone 6 |

## Open items

| Item | Status |
|---|---|
| Build-period dates | Waits for the organisers' guidelines |
| Whether code written before the build period is allowed | Not known. Until it is, keep to wording, data specifications and conversations |
| Where the demo runs: Khalil's laptop or a hosted copy | Waits for the organisers' guidelines |
| Results component in the page | Waits for the organisers' guidelines |
| Test framework and other library choices not named in the architecture | Khalil decides at milestone 1 |
| What the audit log keeps for a balance reply | Proposed: that the balance was shown and when, not the figure. Khalil decides before milestone 7 |
