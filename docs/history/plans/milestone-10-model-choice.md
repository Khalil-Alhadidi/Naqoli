# Design: milestone 10, model choice and freeze

Date: 3 Nov 2026. Author: Claude (Fable 5.1), from a conversation with Khalil. Status: questions answered by Khalil, 3 Nov 2026 (D13 amendment, scripted default); awaiting his approval of the whole.

## Intent

Milestone 10 closes the model question and freezes what the held-out run (milestone 11) and the demo (milestone 12) will use. Three things remain of its "Delivers" list in `docs/build-plan.md`: the chosen model written down and made the default, the Learning data component with its count in the menu, and the Results component showing the latest report's targets table. The comparison report itself is committed (`docs/reports/model-comparison-2026-11-01.md`).

Khalil's rulings of 3 Nov 2026:

- **The model is Sonnet** (`claude-sonnet-5-5`, candidate `sonnet`). On the 1 Nov 2026 comparison it passed 97 of 102 at the median with every target met; Haiku passed 93 and missed one target (asks when it must, 90 % against 95 %); Gemma passed 66 and missed four. Tuning an open-weight model would take time the build period does not have.
- **D13 is amended** (Khalil, 3 Nov 2026) so the rule says what decided this: "Fewest wrong cards wins; on a tie, the model that meets every target; then the cheaper one." Both Anthropic models had 0 wrong cards; Sonnet meets every target and Haiku does not. The amendment, its date and the outcome go in the D13 row of `docs/architecture.md`, and the disclosure cites the amended rule.
- **The committed default parser stays `scripted`** (Khalil, 3 Nov 2026). A fresh clone starts without a key; the README's demo section gives the two user-secrets commands that switch the console to the model. Whether the demo machine ships switched on is a milestone 12 question.
- **One git branch per milestone from now on**, so `main` is always demo-ready. This milestone is built on `milestone-10` and merged when finished, after the security review's fixes.
- **The Learning data panel lets a row be marked Reviewed.** The "save as test case" button named in D12 is not built now; it is recorded as a known gap with a draft-export shape to return to if time remains after milestones 11 and 12.

The build period ends 8 Nov 2026. Milestones 11 and 12 follow this one, so the design keeps to what the build plan names and nothing more.

## Goals

- `Model:Candidate` is `sonnet` by default in the console and the runner, with the reason and the report cited in the docs. Haiku, Opus, Gemini, Gemma and OpenRouter stay selectable.
- The Learning data panel shows the learning log's rows newest first, paged like the other grids, with a Reviewed checkbox per row, and the menu item shows how many rows await review.
- The Results panel shows a committed report's targets table, pass or miss per target, with the run's set, model, prompt version, date, cases, cost and median time, and can show any report in `docs/reports`, the newest by default.
- The retention rule for the learning log (known gap 37) is stated.
- The repository is tagged `milestone-10` with the chosen model, prompt version 6 and seed v4 named in `docs/disclosure.md`; after the tag only plain bug fixes, each listed there.

## Non-goals

- No change to the prompt, the answer keys, the matching rules, the seed or the dev set.
- No "save as test case" button, and no file written by the console (known gap, below).
- No retention timer, no deletion of learning rows by the console, no new setting for retention.
- No comparison across reports in the Results panel: one report at a time, chosen from a list.
- No change to the eval runner's grading or to the Markdown reports. The JSON reports keep their shape byte for byte.
- No sign-in. The Reviewed tick records that someone ticked it, not who.

## Anti-goals

- A panel that reads anything the model could later see. The panels read the agent database and committed report files; nothing flows from them to the parse call.
- A figure on screen that comes from model text. Every value shown is a stored row or a graded report.
- A freeze that is not a freeze. After the tag, a "small improvement" to the prompt or a rule is a breach of milestone 11's premise.

## Constraints (from CLAUDE.md and the architecture)

- Rules 1 to 9 of `CLAUDE.md` unchanged. In particular rule 5: the panels show masked text only, which is what the learning rows hold.
- Everything on the console page outside the phone is English, left to right (D20). No Arabic in C#.
- Limits and page sizes are configuration shown in the settings component.
- Layout B: the new Results item joins the side menu; the selected component renders in the middle; the trace and the phone are untouched.
- Journeys use the scripted parser and the test-id contract in `docs/agent-web.md`.
- The held-out file is not opened, and the Results panel never reads `data/`.

## The design

### Branch, push, merge, tag

`main` is 22 commits ahead of `origin/main`. Push `main` first so the remote matches the demo state, then branch `milestone-10` from it. Work lands on the branch in small commits. At the end: security review, fixes, a `--no-ff` merge into `main`, the tag `milestone-10` on the merge commit, and a push of both. The stale worktree branch `worktree-agent-a3ddadf695db9d739` is merged into `main` already and is removed. Khalil's stash (a `UserSecretsId` on `Agent.Core.csproj`, 28 Oct 2026) is left as it is.

### The chosen model

- `Model:Candidate` becomes `sonnet` in `src/Agent.Web/appsettings.json` and `src/Agent.Eval/appsettings.json`. Nothing else in the candidate entries changes.
- A test pins the default in both files to `sonnet` and to the same value (today `EvalSettingsTests` pins candidate entries; it gains the default).
- `docs/agent-model.md` gets a section "The choice (milestone 10, 3 Nov 2026)": the D13 rule applied to the 1 Nov table, the cost per request, and the fact that Haiku remains one setting away. `docs/architecture.md` D13 gets the outcome and the date. `README.md` and `CLAUDE.md` say `sonnet` where they say `haiku` is the default.
- Any test that assumed `haiku` is the configured default (not one that sets it explicitly) is updated.

### The Learning data panel

A new component `Console/LearningDataPanel.razor` replaces the `Placeholder` for `ConsolePanel.LearningData` in `Home.razor`. It follows `LogPanel.razor` in shape: a `MudPaper` with a title, a refresh button, a loading skeleton, an empty state, a table and a `GridPager`.

Reading. A method on `LearningLog` in `Agent.Core`, `ReadLatestAsync(int take, CancellationToken)`, returns the newest `take` rows ordered by `At` descending then `Id` descending, no tracking. The panel reads it through a scope, as `LogPanel` reads `AuditLog`, and loads `Agent:LogPageSize` rows (100 today), the same setting the Log panel uses for how many rows it loads from the database; the grid pager then pages them by `Console:GridPageSize`. No new setting.

Columns, in this order: At (local time of the console, `yyyy-MM-dd HH:mm:ss`), User (`U<n>`), Reason, Detail, Masked text, Reviewed. The masked text is shown as stored, with its `<NUM_n>` tokens; it is right-to-left text inside a left-to-right grid, so the cell gets `dir="auto"`. Long text wraps.

Reviewed. The last column is a checkbox bound to the row. Ticking or unticking calls `LearningLog.SetReviewedAsync(long id, bool reviewed, CancellationToken)`, which updates the one row and saves. A row that no longer exists (reset in between) is a no-op and the panel reloads. The row's `data-reviewed` attribute reflects the stored value after the save, not before.

Count in the menu. The `Learning data` menu item shows the number of rows with `Reviewed` false, as a `MudBadge` or a plain span with `data-testid="menu-learning-count"`, hidden when the count is 0. The count comes from `LearningLog.CountUnreviewedAsync`. It refreshes on the same signals `LogPanel` uses to refresh itself (the chat session's reply event), on a tick in the panel, and when the panel is selected. The builder decides the mechanism; the layout already subscribes to `ConsoleNavigation.Changed`.

Refresh and reset. The refresh button rereads. The console's Reset panel keeps the learning log, as its comment says; this does not change. The runner and the journeys clear it before each case, as today.

Test ids: `learning-panel`, `learning-refresh`, `learning-row` with `data-id`, `data-reason`, `data-user` and `data-reviewed`, `learning-reviewed` (the checkbox, with `data-id`), `menu-learning-count`; the pager's `data-grid` is `learning`.

### The retention rule (known gap 37)

Stated, not enforced by a timer, in `docs/agent-model.md` (learning log) and `docs/known-gaps.md` row 37: in this mock, learning rows live until the agent database is reset by the runner or the journeys, or recreated; the console's Reset keeps them because the demo shows them. The rule for a deployment against a real wallet would be a deletion after a fixed period from `At` once `Reviewed` is true, with the period a setting; it is not built because the mock holds synthetic text only. Row 37 moves from "a rule stated when the view arrives" to "rule stated; a deletion job is a deployment item".

### The Results panel

A new `ConsolePanel.Results` and a menu item `Results` (test id `menu-results`, icon `Assessment`) between Log and Aliases, and a component `Console/ResultsPanel.razor`.

Where the reports are. A console setting `Console:ReportsDirectory`, default `docs/reports`, resolved through `RepositoryRoot.Find()` as `Agent:DevSetPath` is, shown read-only in the settings component under Console. The panel lists every `*.json` in it whose name begins `dev-` or `heldout-` and whose document has a `targets` array (the 27 reports of 15 and 16 Oct 2026 predate the targets table and are left out; the panel says how many files it skipped), reads each file's `header` (set, parser, started), and offers them in a select newest first by `header.started_utc`, a repeat aggregate ahead of its own runs when the start is equal. The Markdown twins are not read. A directory that does not exist or holds no report shows an empty state ("No reports in <directory>.") rather than an error.

Which report opens first. Not simply the newest file: today that is a single Gemma run with four missed targets, and the demo should not open on it. The default is the newest report whose parser matches the configured candidate (`claude-sonnet` for `Model:Candidate` `sonnet`), an aggregate before its runs; if none matches, the newest overall. After milestone 11 the held-out report of the chosen model is the newest and opens first.

Two shapes. A single run (`cases`, `targets` with `value` and `verdict`) and a repeat aggregate (`runs`, `targets` with `min`, `median`, `max`). The panel renders the header block for both, then the targets table: Measure, Target, Value (or Min / Median / Max for an aggregate), Verdict as a `StatusBadge`-style chip (pass green, miss red, reported neutral). Under it, for a single run, the totals line: cases, passed, percent, cost in USD, median time in seconds. For an aggregate, a small per-run table (run, passed of cases, cost, median time) taken from `runs[]`, and the group line passed min / median / max: the console computes nothing, since the aggregate holds no overall cost or time. A file that fails to parse shows "Could not read <file>" and the next report can still be chosen.

The contract. `ReportWriter` in `Agent.Eval` builds anonymous objects today. The document types move to `Agent.Core` (`Agent.Core.Reports`: `ReportDocument`, `RepeatReportDocument`, their rows) so the writer serialises them and the panel deserialises them with one set of JSON options (explicit nulls kept, as the writer does today). The output must stay byte-identical for the current shape: a test deserialises every committed report that has a `targets` array into the new types and re-serialises it to the same text, and the existing `ReportWriterTests` keep passing on the same expected text. The 27 older reports without `targets` are not part of the contract and are not read by the panel. `Agent.Web` does not reference `Agent.Eval`.

Test ids: `results-panel`, `results-select`, `results-header` with `data-set`, `data-parser` and `data-started`, `results-target` with `data-measure` and `data-verdict`, `results-totals`, `results-error`.

### Journeys

`ConsoleJourneys` gains, on the U1 console:

- Learning: say ADD-02 (an unknown name, learning `recipient_not_found`), open `menu-learning`, see one `learning-row` with that reason and `data-reviewed="false"` and the menu count `1`; tick it; see `data-reviewed="true"` and the count gone; refresh; still ticked.
- Learning paging: with page size at its minimum via the settings component, more rows than one page shows the pager with `data-grid="learning"` (only if a dev case sequence produces enough rows cheaply; otherwise the pager is covered by the component test and this journey is dropped).
- Results: the journey host points `Console:ReportsDirectory` at a fixture directory in the test project holding two small committed report JSON files (a single run and a repeat aggregate, trimmed to a few cases), opens `menu-results`, sees the newest selected, nine `results-target` rows with their verdicts, switches to the other file, sees the aggregate columns.
- The two single-read journeys noted in `docs/agent-web.md` (`RequestJourneys.NoneOfTheseOnWhichContactRefusesRecipientNotFound`, `FaultJourneys.ModelInvalidReplyIsNotUnderstood`) get the polling read in `JourneyBase.LearningRowsAsync` that the note proposes, since this milestone touches that read.

### Tests in `Agent.Core.Tests`

- `LearningLogTests`: `ReadLatestAsync` order and take; `SetReviewedAsync` on a present and an absent id; `CountUnreviewedAsync`.
- `Reports` round trip: every `docs/reports/*.json` deserialises into the document types and re-serialises to the same text (or, if indentation differs, to the same JSON tree).
- `EvalSettingsTests`: the default candidate is `sonnet` in both settings files.
- A `ResultsPanel` reading test on the fixture directory (through a small service `ReportReader` in `Agent.Web/Services`, so the file listing and parsing are testable without a browser): newest first, both shapes, missing directory, unparseable file.
- `ConsoleOptions` validation: `ReportsDirectory` not empty.

### Documentation

- `docs/agent-web.md`: the two panels, their test ids, the menu count, the Results select; the placeholder rows removed; the Mermaid layout diagram updated; a Mermaid diagram of the Reviewed tick and the count.
- `docs/agent-model.md`: the choice; the learning log's retention rule.
- `docs/agent-eval.md`: the report document types now live in `Agent.Core.Reports`, and the console reads them.
- `docs/architecture.md`: D13 amended as ruled, with the outcome and the date; D17 "results component" no longer optional; known-gaps references.
- `docs/known-gaps.md`: row 20 closed; row 37 rule stated; a new row for the D12 "save as test case" button, with the draft-export shape (a separate `data/requests.dev.drafts.jsonl` holding the masked text, user, reason and date with an empty expected block for Khalil, loaded by nothing).
- `docs/failure-modes.md`: a report file that cannot be read; a Reviewed tick on a row that was reset.
- `docs/disclosure.md`: a "Freeze, milestone 10" section naming the model id, prompt version 6, the seed hash, the tag, and a list headed "Fixes after the freeze" that starts empty.
- `docs/build-plan.md`: milestone 10's "Done when" lines marked with what shows them.
- `README.md` and `CLAUDE.md`: the default candidate, the two new panels, the new setting; the README's demo section lists the two user-secrets commands that turn the model on, since the committed parser stays `scripted`.

### What the freeze covers

The build plan's "only fixes to plain code bugs after the tag" names the surface that decides the held-out result, not the whole repository, since milestones 11 and 12 follow the tag by design. Frozen from the tag: the prompt (`parse-prompt.md`), the candidate entries and the default candidate, the parsing, validation, resolver, conversation and grading code in `Agent.Core` and `Agent.Eval`, `docs/matching-rules.md`, the seed, the dev set and its parses. A change there after the tag is a bug fix listed under "Fixes after the freeze" in the disclosure, with the reason. Free after the tag: the console's look and panels, the journeys, the docs, the README, the demo script, the one commit that lifts the held-out deny rule for milestone 11 and the commit of its report. The disclosure's freeze section says this in two lines.

### Security review and close

At the end, the `dotnet-security-reviewer` agent reads the diff of the branch. Its findings are fixed on the branch before the merge; the review and its disposition go to `docs/history/reviews/milestone-10-security-review.md` in the repository's existing format. Then the merge, the tag and the push.

## Calls made in this design (Claude's, for Khalil to overturn)

1. The menu count is the number of unreviewed rows, not the total, so the Reviewed tick has a visible effect.
2. The Learning data panel loads `Agent:LogPageSize` rows, the Log panel's setting, rather than a new setting.
3. The console's Reset keeps learning rows, as today.
4. The Results panel reads committed JSON files from `docs/reports` through a new read-only console setting `Console:ReportsDirectory`, and offers every report in a select rather than only the newest.
5. The report document types move to `Agent.Core` so the console and the runner share one contract; the JSON stays identical.
6. The Results menu item sits between Log and Aliases.
7. The polling fix for the two single-read journeys is included, since the learning read is touched anyway.
8. The retention rule is stated, not built.
9. `main` is pushed before the branch is cut.
10. The Results panel skips the 27 reports from before the targets table, opens on the newest report of the configured candidate rather than the newest file, and shows an aggregate's per-run figures as stored instead of computing a total.
11. The freeze covers the parsing surface, the rules, the seed and the sets, not the console, the docs or the milestone 11 and 12 commits.

## Left to the builder

- The refresh mechanism for the menu count (event, timer on navigation, or both), following `LogPanel`'s pattern.
- The exact record shapes in `Agent.Core.Reports` and the JSON options, as long as the committed reports round-trip.
- The fixture reports for the journeys (derived from committed reports, trimmed).
- Icon choices and table widths, within the existing tokens.
- The order of commits, one per task of the plan.
