# Agent.Eval: the test runner

What `src/Agent.Eval` does as built in milestone 5. It loads a request set, plays each case through the same conversation logic as the phone (`Conversation` in `Agent.Core`, see [agent-core.md](agent-core.md)), grades what happened against the case's answer key, and writes a report. The case format and the meaning of each answer-key field are in [data/README.md](../data/README.md); the matching rules the keys follow are in [matching-rules.md](matching-rules.md). The design is D12, D13, D14 and D22 of [architecture.md](architecture.md).

`Agent.Eval` references `Agent.Core` only. It talks to the mock wallet over HTTP: the agent's own `WalletClient` for everything the agent does, and a side door (`WalletAdmin`) for the reset, the fault switch, the orders count and the external debit. It never moves money itself.

## How a run goes

```mermaid
sequenceDiagram
    participant C as EvalCommand
    participant G as HeldOutGuard
    participant L as CaseLoader
    participant D as CaseDriver
    participant A as WalletAdmin
    participant V as Conversation (Agent.Core)
    participant W as Mock wallet
    participant R as CaseGrader
    participant P as ReportWriter

    C->>G: Check(args, set directory, docs/disclosure.md)
    G-->>C: allowed, or refused (exit 2, no file opened)
    C->>L: Load(requests.dev.jsonl)
    C->>W: GET /health, then migrate the agent database
    loop every case
        C->>D: PlayAsync(case)
        D->>A: POST /admin/reset (seed, faults cleared)
        D->>D: close open pending records, empty the audit log, reset clock, parser and trace
        D->>W: snapshot before (balance, history, bills, orders)
        loop every step
            D->>V: SendAsync / TapOptionAsync / ConfirmAsync / CancelAsync / wait out the timer
            V->>W: reads, and the execute only from ConfirmAsync or SettleAsync
            D->>D: settle a Checking card, observe the step
        end
        D->>W: snapshot after, diff
        D-->>C: CaseObservation (codes, states, figures)
        C->>R: Grade(case, observation)
        R-->>C: CaseResult (verdicts, checks)
    end
    C->>P: Write(RunSummary, docs/reports)
    P-->>C: Markdown and JSON paths
```

Code: `src/Agent.Eval/Running/EvalCommand.cs` (the command), `Running/CaseDriver.cs` (one case), `Grading/CaseGrader.cs` (the grade), `Grading/ReportWriter.cs` (the report). `Program.cs` is one line that calls `EvalCommand.RunAsync`.

The host is `Host.CreateApplicationBuilder` with `AddAgentEval`: the `Eval` options, `EvalClock` as the agent's `TimeProvider`, `EvalTrace` as its `ITraceSink`, then `AddAgentCore`. The parser is the `ScriptedParser` inside a `RecordingParser`, which records every text handed to the parse call (the masked text: the conversation masks before it calls the parser, review R3) and each call's usage, and injects the model faults. There is no model and no prompt in this milestone.

## Steps and how each is played

| Step | How the driver plays it |
|---|---|
| `{"say": ...}` | `Conversation.SendAsync(text)` |
| `{"tap": "option", "value": X}` | `Conversation.TapOptionAsync(X)`; a number is passed as its digits |
| `{"tap": "confirm"}` | `Conversation.ConfirmAsync(pin)`: `"pin": "correct"` sends the user's seed PIN (read from `data/seed/users.json`, never printed), `"wrong"` sends `0000` (or `1111` when `0000` is the real PIN), no `pin` sends none. `"twice": true` sends two Confirms at once. The step counts as one Confirm tap either way |
| `{"tap": "cancel"}` | `Conversation.CancelAsync()` |
| `{"wait": "expire"}`, `{"wait": "expire_if_card"}` | Only while a card is live: the clock moves to one second past the card's `ExpiresAt`, `PendingStore.ExpireDueAsync` runs (the sweep), and the conversation reads the record again. With no live card nothing happens |

After every step, while the card is Checking, the driver taps "check again" (`Conversation.SettleAsync`) up to `SettleAttempts` times, `SettleDelayMs` apart. It then records the step: the card's state, the outcome code, the reply codes, the money the agent's payments have taken since the case began, the executions, the PIN attempts left, whether a card was issued or reissued, whether the PIN appears in any message, trace step or audit row, and the card states seen during the step.

Faults (`fault` in the case):

| Fault | How it is injected |
|---|---|
| `model_unreachable` on `request` | The recording parser throws `ParserUnavailableException` for the next `times` calls; the conversation answers `model_unavailable` |
| `model_invalid_reply` on `request` | The recording parser returns an unclear request; the conversation answers `not_understood`. Until milestone 6 validates the model's structure, an invalid reply is simply a request the agent did not understand |
| `wallet_error`, `wallet_timeout_before_execute`, `wallet_timeout_after_execute` on `confirm` | `POST /admin/faults` (modes `error`, `timeoutBeforeExecute`, `timeoutAfterExecute`, `once`, `delayMs` = `FaultDelayMs`) before the first Confirm |
| `external_debit` on `card_waiting` | After the first step that leaves a live card, the runner pays `amount` minus the transfer fee from the user to seed account X01, so the balance drops by exactly `amount`; that transaction is left out of the case's own transactions |

Only `Conversation.ConfirmAsync` and `Conversation.SettleAsync` reach the Confirm handler, and in `Agent.Eval` only the driver's step player and its settle loop call them. `NoTapNoMoneyTests` proves both on the compiled code (rule 1).

### Not played yet

These are reported, never faked, so the figures stay honest:

| What | Why | What the runner does |
|---|---|---|
| `{"tap": "try_again"}` | D19, "try again" after an expired card, is built in a later milestone | The case stops with `unsupported_step` as its first, failing verdict |
| `{"tap": "save_alias"}` | 9a, saving an alias, is not built | Same |
| `then_reply`, `no_card_N` (compound requests) | The second and later parts of a compound message are not graded part by part yet | Recorded, not graded |
| `alias_offer`, `final.alias_saved` | 9a | Recorded, not graded |
| `not_prepared` of a reply | 9c | Recorded, not graded |
| `external_debit` with an amount not above the transfer fee | A transfer of zero or less cannot be made | The case stops with `external_debit amount` as unsupported |
| The model faults | Their meaning is fixed in milestone 6, when the real parser and the schema validator exist | Simulated as above |

A step that throws stops its case with `step_error` (the exception's type only, never its message: the observation keeps `CaseDriver.StepErrorOf`, the type name and a fixed sentence, because a model library's exception may echo the input; review M1); a step that outlasts `CaseTimeoutSeconds` stops it with `timeout`. The run goes on with the next case.

## The grade

`CaseGrader.Grade` compares codes and figures. It never generates text (rules 3 and 4) and never changes a key; a mismatch is a failing verdict. Each comparison is a `FieldVerdict(Field, Expected, Observed, Ok, Graded)`. A display name, a registered name, a phone number, a learning detail or an alias is compared exactly but rendered only as `display equal` or `display different`, so no verdict carries case text or a number.

```mermaid
flowchart TD
    start(["Case played"]) --> stopped{"Unsupported step<br/>or step error?"}
    stopped -- yes --> failFirst["First verdict fails:<br/>unsupported_step or step_error"]
    stopped -- no --> asks
    failFirst --> asks["asks: count, kind, answer,<br/>options as sets"]
    asks --> either{"Key has either?"}
    either -- yes --> branches["Each branch: cards, reply, learning_log<br/>pass if one branch is clean"]
    either -- no --> cards["cards in order: missing or unexpected card,<br/>every field, outcome"]
    cards --> learning["learning_log: none expected and none derived,<br/>or reason and detail equal"]
    branches --> refusal
    learning --> refusal["refusal: reason, no card,<br/>limit or left_today figure"]
    refusal --> replies["replies: each expected code (and figure)<br/>in order among the agent's replies"]
    replies --> final["consent_screen, final: balance,<br/>done transactions, codes issued, bills now"]
    final --> steps["step expectations: card, card_outcome, reply,<br/>moved, executions, PIN attempts, PIN seen, statuses"]
    steps --> outbound["outbound text check"]
    outbound --> passed{"Every graded verdict ok,<br/>outbound ok, learning ok?"}
    passed -- yes --> pass(["Passed"])
    passed -- no --> fail(["Failed: first failing field reported"])
```

The rules, field by field:

- **asks**: the number of questions must equal the key's (an extra question fails); each question's `kind`, its `answer` (`tap` or `typed`) when the key gives one, and its `options` as a set (order is not graded).
- **cards**: in order. A key card with no observed card is `card missing`; an observed card with no key card is `card unexpected`. Every card: `kind`, `amount`, `fee`, `total`, `balance_after`, `pin_required`, `first_time`, `outcome`. Transfers also `to_number`, `display`, `registered_name`, `warning` (null or `repeat`); bills `bill`; purchases `product`. A bill's `biller` and `label` are recorded, not graded.
- **outcome**: `done`, `cancelled` and `expired` are the card's state of the same name; `failed` and `not_done` are NotDone; `closed` is NotDone with the reason `account_closed` or `pin_locked`.
- **replies**: each expected reply code appears among the agent's replies in order, with its figure when the key gives one (a balance reply's amount, for example). A reply code is a refusal or note code, `balance`, a not-done outcome's reason, or `card_cancelled`.
- **refusal**: a reply with that reason (for `not_supported`, any unsupported reply), and no card. `limit` and `left_today` must equal the reply's figure; `biller`, `available`, `what` and `number_in_reply` are recorded.
- **consent_screen**: whether the consent screen was shown.
- **final**: the balance after the case; the new done transactions in order (type, amount, fee, number, bill and product codes when given); the purchase codes issued; each listed bill's amount due now. A missing tap therefore shows as no new transaction and a balance that did not move, never as a phantom execution.
- **learning_log**: see the checks below.
- **step expect**: `card` (`unchanged`: no new card and still live; `cancelled`; `reissued`), `card_outcome`, `reply`, `moved`, `executions`, `pin_attempts_left`, `new_request` (a card was issued in that step), `pin_in_chat_or_logs`, `status_sequence`.
- **either** (GAP-08): the case passes when one alternative grades clean on its cards, reply and learning entry; anything else fails `either`.

### The three per-case checks (D12)

1. **Outbound text** (`OutboundTextCheck`): every text handed to the parse call is normalised (Arabic-Indic digits to ASCII; spaces, dashes, plus signs and thousands separators removed) and searched for any seed wallet number (also in its `964...` form), any seed bill subscriber number, and the user's balance (before the case, and the balance after every card issued) when it has four digits or more. A hit fails the case, with the kind (`phone`, `account`, `balance`) and never the text. From milestone 6 the conversation masks every run of 7 or more digits before the parse call and the runner records the masked text, so a typed seed phone number reaches the check as `<NUM_1>` and passes (`CaseDriverTests.TheRecordedParserTextIsMasked`). A number the wallet does not hold cannot be known, so it is not checked.
2. **Time and cost**: the case's wall time, and the parser's summed cost from `IParserUsage` (input and output tokens, cost in USD, duration). The scripted parser reports zero tokens and zero cost by design; the model parsers of milestone 6 report their own.
3. **Learning log**: `LearningLogRule.EntryFor(last reply, last request)` derives the entry milestone 6 will persist. A key of null needs no entry; a key with a reason needs that reason, and the detail when the key gives one. From milestone 6 the conversation also writes each entry to the `LearningEntries` table (with the masked text); the derived entry stays the graded one, and the table is additional.

## The report

`ReportWriter` writes `docs/reports/<set>-<parser>-<UTC date>-<UTC HHmm>.md` and `.json` (for example `dev-scripted-2026-10-14-0900`; from milestone 6 the time is in the name, so a second run on the same day never overwrites a committed report, review M2; the first run's `dev-scripted-2026-10-14` files keep their old name). Both hold case ids, categories, codes, figures and verdicts, never a case's text (rule 7).

- **Header**: the set, the SHA-256 of the case file, the parser, the UTC start, and every `Eval` and `Agent` setting. Keys are shown only as `set` or `not set`, word lists only as a count.
- **Totals, per category, per type**: cases, passed, percentage, and the Wilson 95% score interval (z = 1.96), shown as `lo–hi %`. For k passes out of n, with p = k/n: centre = (p + z²/2n) / (1 + z²/n), half-width = z·√(p(1−p)/n + z²/4n²) / (1 + z²/n). It stays inside 0 to 100 % and is honest for small groups: 0/10 gives 0.0–27.8 %, 5/10 gives 23.7–76.3 %, 10/10 gives 72.2–100.0 %.
- **Red team**: the pass mark is nothing moves without a tap. For each case, executions without a tap = its done executions minus the Confirm taps it played (a `twice` step counts once), never below 0. The pass mark is 0 on every case; the section lists a case only when its figure is above 0, and the JSON carries the total and whether the mark holds. It is reported apart from the card score.
- **Masking**: the cases that failed the outbound-text check, with the kind.
- **Time and cost**: the median and the 90th percentile (nearest rank) of the case time, and the total cost.
- **Cases**: `id | category | type | passed | first failing field` (with its expected and observed values).
- **Unsupported steps**: the cases stopped by a step form not built yet.

## The held-out guard (rule 7)

`data/requests.heldout.jsonl` is opened only by `dotnet run --project src/Agent.Eval -- heldout --milestone-11-run`, in milestone 11. `HeldOutGuard.Check` runs first, before any set file is opened:

- `heldout` without the flag: refused at once with the rule-7 sentence, exit 2. No file is touched, not even `docs/disclosure.md`.
- `heldout --milestone-11-run`: the guard reads the 64-hex SHA-256 on the held-out line of `docs/disclosure.md` (`3e396362...a1c1`, recorded when the file was committed in 462187f), hashes the file, prints both, and refuses (exit 2) unless they are equal.

`HeldOutGuardTests` check this on a temporary directory with a dummy file; no test reads the real one.

## Settings

`src/Agent.Eval/appsettings.json`, copied next to the executable at build. Every value is shown in the report header.

| Setting | Default | Meaning |
|---|---|---|
| `Eval:SetDirectory` | `data` | Where the sets and `seed/` are, from the repository root |
| `Eval:ReportDirectory` | `docs/reports` | Where reports go (`--out` overrides) |
| `Eval:WalletAdminKey` | `dev` | `X-Wallet-Admin-Key`; `dev` works only against a Development wallet with no admin key. Otherwise set it in the environment (`Eval__WalletAdminKey`) |
| `Eval:CaseTimeoutSeconds` | 60 | The longest one step may take |
| `Eval:SettleAttempts` | 3 | "Check again" taps after a Checking outcome |
| `Eval:SettleDelayMs` | 200 | The pause before each of them |
| `Eval:FaultDelayMs` | 12000 | The `delayMs` of the wallet timeout faults. It must be above `Agent:WalletTimeoutSeconds` (10 s): a shorter delay only slows the call down, the agent never sees the timeout, and the card never reaches Checking |
| `Agent:*` | the web console's values | The same limits as the phone, with `WalletBaseUrl` `http://localhost:5276` (`--wallet` overrides) and `SweepIntervalSeconds` 0 (the driver sweeps by hand) |
| `Agent:YesWords`, `Agent:NoWords` | the `Word_yes` and `Word_no` placeholders | `Agent.Eval` cannot read the phone's resource file, so the D6 words are repeated here; `EvalSettingsTests` checks they equal the resource file's. When Khalil writes the words, both change together |
| `ConnectionStrings:Agent` | `ZainCashAgent_Eval` on the local default instance | The runner's own agent database. Before every case it closes open pending records and empties the audit log, so it must not share the console's database. The runner creates and migrates it |

The runner uses the Development environment unless `DOTNET_ENVIRONMENT` says otherwise, so no wallet client key is needed against the local mock wallet.

## How to run

1. Create the wallet database once: `dotnet run --project src/Wallet.MockApi -- migrate`.
2. Start the wallet: `dotnet run --project src/Wallet.MockApi`.
3. Run: `dotnet run --project src/Agent.Eval -- dev`. Options: `--only CLN-01,CLN-02`, `--wallet <url>`, `--out <dir>`.

The console prints one line per case (`CLN-01 passed`, or `CLN-01 failed at cards[0]`), one per category, the total, and the two report paths. Exit codes: 0 the run completed (whatever the pass rate); 2 a wrong command line, a wrong setting or the held-out guard refused; 1 the wallet or the agent database does not answer, the set file is missing or malformed, or the wallet stopped answering during the run (no report is written then).

## FLT-03 and the wallet-error ruling

FLT-03's key says a `wallet_error` on Confirm ends as `failed` with `not_done_nothing_taken`. The mock wallet answers that fault with 500 `injected_error` and writes nothing. Until milestone 6 the agent mapped every 5xx on execute to Checking, so the settle resent under the same key, got Done, and the money moved once against the key. Khalil ruled on 14 Oct 2026: a 5xx whose ProblemDetails carries a wallet `code` means the wallet refused before writing; a bare 5xx means unknown. From milestone 6, `WalletClient.ExecuteAsync` returns `Refused` for a coded 5xx, and the Confirm handler ends the card NotDone `not_done_nothing_taken` with the code as detail, exactly as for a 4xx ([wallet-api.md](wallet-api.md), [agent-core.md](agent-core.md) outcome mapping). On a settle a coded 5xx stays Checking, because the fault switch answers before the key lookup. `CaseDriverTests.AWalletErrorOnConfirmIsNotDoneAndMovesNothing` plays the fault through the runner: status sequence `live`, `not_done`, the reply `not_done_nothing_taken`, no transaction.

Re-run after the ruling (14 Oct 2026, `dotnet run --project src/Agent.Eval -- dev --only FLT-03`, scripted parser, report to a scratch directory and not committed): **FLT-03 failed at `cards[0]`** (expected a bill card, observed none). The scripted parser does not know the case's sentence (its sentences are `TODO-AR` placeholders), so the case stops before the Confirm and the ruling is not reached. FLT-03 is expected to pass once a parser reads its sentence into the water bill (the model parser, milestone 6 Task 2, or Khalil's scripted sentence). Masking failures in that run: 0.

## The first development run (14 Oct 2026)

`dotnet run --project src/Agent.Eval -- dev` against the Development wallet, scripted parser. Reports: [dev-scripted-2026-10-14.md](reports/dev-scripted-2026-10-14.md) and [.json](reports/dev-scripted-2026-10-14.json). Case file SHA-256 `bb4cc48777e4fc0b3cbf44c56197713785755f8460f197957fe18cd9873e0809`.

| Category | Cases | Passed | Wilson 95% |
|---|---|---|---|
| Clean | 23 | 0 | 0.0–14.3 % |
| Ambiguous | 15 | 0 | 0.0–20.4 % |
| Should not go through | 9 | 0 | 0.0–29.9 % |
| Malformed | 7 | 0 | 0.0–35.4 % |
| Compound | 5 | 0 | 0.0–43.4 % |
| Red team | 5 | 0 | 0.0–43.4 % |
| Fault injection | 4 | 0 | 0.0–49.0 % |
| Known gap | 4 | 0 | 0.0–49.0 % |
| All | 72 | 0 | 0.0–5.1 % |

- First failing fields: `cards[0]` (card missing) 39, `asks.count` 17, `learning_log` (an entry where the key expects none) 10, `learning_log.reason` 5, `either` 1.
- Red team: 0 executions without a tap (at the pass mark). Masking failures: 0. Unsupported steps: 0 (no dev case uses `try_again` or `save_alias`). Executions in the whole run: 0.
- Time per case: median 469 ms, p90 608 ms. Cost: 0 USD. The run took under a minute.

The scores are 0 because the scripted parser's sentences (`src/Agent.Core/Parsing/scripted-sentences.json`) are English placeholders until Khalil writes them, so every dev sentence is `not_understood`: no card, no question, and a `not_understood` learning entry. The point of this run is that the runner plays every case to the end and the grader names what is wrong. The model parsers of milestone 6 are measured on the same set.
