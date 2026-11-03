# Milestone 10 Implementation Plan: model choice and freeze

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Sonnet the default model, build the Learning data and Results panels of the console, state the learning log's retention rule, and leave the branch ready to merge, tag `milestone-10` and freeze.

**Architecture:** Two new Blazor Server panels in `Agent.Web` read, through scoped services, the agent database (`LearningLog`) and the committed eval reports (`ReportReader` in `Agent.Core.Reports`, which also owns the target-row records the runner writes). No change to parsing, grading, the prompt, the seed or the sets. Every value shown is a stored row or a graded report.

**Tech Stack:** .NET 10, Blazor Server, MudBlazor 9, EF Core on SQL Server, `System.Text.Json`, xUnit (`Agent.Core.Tests`, collection `agent` with the `AgentHost` fixture), Playwright for .NET (`Agent.Web.Journeys`, scripted parser).

**Spec:** `docs/plans/milestone-10-model-choice.md` (approved by Khalil, 3 Nov 2026).

## Global Constraints

- Work on branch `milestone-10` (cut by the controller from `main` after `main` is pushed). Small commits, one per task or less. Commit messages carry Khalil's name only: no `Co-Authored-By` or session trailer (CLAUDE.md working habits and the 15 Oct 2026 rule).
- Rules 1 to 9 of `CLAUDE.md`. Never open, grep or print `data/requests.heldout.jsonl` or any `.xlsx`.
- No Arabic in C#; the console is English, left to right. No new phone strings are needed.
- The `data-testid` contract in `docs/agent-web.md` is extended, never changed, for existing ids.
- Limits are configuration shown in the settings component: the one new setting is `Console:ReportsDirectory`, default `docs/reports`, read only.
- The JSON reports keep their shape byte for byte: `ReportWriterTests` keep passing unchanged.
- `Agent.Web` does not reference `Agent.Eval`. `Agent.Core` does not reference either.
- Every "Done when" claim at the end names the test or file that shows it.
- Commands run from the repository root. Full suite before the hand-back: `dotnet test` (expect 0 failed; the database and Chromium gates may skip).

## Review Focus

Inputs the spec implies but its text does not pin; each has its test in the owning task.

1. A Reviewed tick on a row that a reset deleted between the read and the tick must not throw; the panel reloads and the row is gone (Task 2 `SetReviewedAsync` on an absent id; Task 3 panel catch).
2. A learning row's masked text is Arabic inside an English grid; it must render right to left without flipping the row (Task 3 `dir="auto"` on the cell; journey asserts the text).
3. A report file in the directory that is not a report (a stray JSON, or an old shape without `targets`) must be skipped and counted, not crash the listing (Task 4 `ListSkipsFilesWithoutTargets`, `ListSkipsUnparseableFile`).
4. Two reports with the same `started_utc` (an aggregate and its run 1) must order deterministically with the aggregate first (Task 4 `AggregateComesBeforeItsFirstRunOnEqualStart`).
5. A configured candidate with no report at all (a fresh clone with an empty directory, or `heldout` only) must still open on something or show the empty state, never throw (Task 4 `DefaultFallsBackToNewestWhenNoParserMatches`, `ListOfMissingDirectoryIsEmpty`).

---

### Task 1: Sonnet is the default candidate

**Files:**
- Modify: `src/Agent.Web/appsettings.json` (`Model:Candidate`), `src/Agent.Eval/appsettings.json` (`Model:Candidate`)
- Modify: `docs/agent-model.md`, `docs/architecture.md` (D13 row), `README.md`, `CLAUDE.md` (the lines that say `haiku` is the default)
- Test: `tests/Agent.Core.Tests/Eval/EvalSettingsTests.cs`

**Interfaces:**
- Produces: nothing for later tasks; the Results panel's default rule (Task 4) reads `ModelOptions.Candidate`, which is now `sonnet`.

- [ ] **Step 1: Write the failing test** in `EvalSettingsTests`:

```csharp
[Fact]
public void TheDefaultCandidateIsSonnetInBothProjects()
{
    using var eval = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "src", "Agent.Eval", "appsettings.json")));
    using var web = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "src", "Agent.Web", "appsettings.json")));
    Assert.Equal("sonnet", web.RootElement.GetProperty("Model").GetProperty("Candidate").GetString());
    Assert.Equal("sonnet", eval.RootElement.GetProperty("Model").GetProperty("Candidate").GetString());
}
```

- [ ] **Step 2: Run it and see it fail** (`haiku` ≠ `sonnet`):

Run: `dotnet test tests/Agent.Core.Tests -- --filter-class "Agent.Core.Tests.Eval.EvalSettingsTests"`

- [ ] **Step 3: Set `"Candidate": "sonnet"`** in both `appsettings.json` files. Nothing else in the `Model` section changes.

- [ ] **Step 4: Run the test class and the parsing tests**; grep `tests/` for `"haiku"` and check each hit sets the candidate explicitly (they do today: `ParserFactoryTests`, `ClaudeParserTests`, `AnthropicAdapterTests`, `LiveModelTests`, `HeldOutGuardTests`, `ReportWriterTests`) so none assumes the file default.

Run: `dotnet test tests/Agent.Core.Tests`
Expected: 0 failed.

- [ ] **Step 5: Docs.** In `docs/architecture.md` D13: append to the decision cell the amendment and the outcome, verbatim: *"Amended 3 Nov 2026 (Khalil): fewest wrong cards wins; on a tie, the model that meets every target; then the cheaper one. Outcome, 3 Nov 2026: Sonnet (`claude-sonnet-5-5`), on `docs/reports/model-comparison-2026-11-01.md`: Haiku and Sonnet 0 wrong cards; Sonnet meets every target, Haiku misses "asks when it must" (90 % against 95 %)."* In `docs/agent-model.md` add a section `## The choice (milestone 10, 3 Nov 2026)` with the amended rule, the table line (Sonnet 97 / Haiku 93 / Gemma 66 of 102 at the median), the cost per completed request from the report (Sonnet 0.0018 USD, Haiku 0.00012 USD), and the sentence that Haiku stays one setting away (`Model:Candidate`). In `README.md` and `CLAUDE.md`, replace the `haiku` default wording with `sonnet` (CLAUDE.md: the "Model parser (milestone 6...)" bullet: `candidate Model:Candidate: sonnet default, haiku, opus, ...`). README's demo section (the "The model" section) keeps `scripted` as the committed default and shows the two `dotnet user-secrets set` commands as the way to switch on; add one sentence that this is what the demo laptop runs.

- [ ] **Step 6: Commit**

```bash
git add src/Agent.Web/appsettings.json src/Agent.Eval/appsettings.json tests/Agent.Core.Tests/Eval/EvalSettingsTests.cs docs/agent-model.md docs/architecture.md README.md CLAUDE.md
git commit -m "config: sonnet is the default candidate (D13 amended by Khalil, 3 Nov 2026: fewest wrong cards, then every target met, then cheaper); EvalSettingsTests pins both files; agent-model.md records the choice"
```

---

### Task 2: The learning log can be read, counted and marked reviewed

**Files:**
- Modify: `src/Agent.Core/Learning/LearningLog.cs`
- Test: `tests/Agent.Core.Tests/Learning/LearningLogTests.cs`

**Interfaces:**
- Produces, on `LearningLog` (scoped service, already registered):
  - `Task<IReadOnlyList<LearningEntryRow>> ReadLatestAsync(int take, CancellationToken cancellationToken)`: newest `take` rows by `At` descending then `Id` descending, no tracking; throws `ArgumentOutOfRangeException` for `take < 1` (mirror `AuditLog.ReadLatestAsync`).
  - `Task<bool> SetReviewedAsync(long id, bool reviewed, CancellationToken cancellationToken)`: updates the one row and saves; returns `false` without throwing when no row has `id`.
  - `Task<int> CountUnreviewedAsync(CancellationToken cancellationToken)`: rows with `Reviewed == false`.

- [ ] **Step 1: Write the failing tests** in `LearningLogTests` (use the existing `WriteAsync` helper and `host.Services` scope; clear the table first as the other tests do):

```csharp
[Fact] public async Task ReadLatestReturnsNewestFirstAndTakesAtMost()
// write 3 rows with distinct reasons; ReadLatestAsync(2) → 2 rows, first has the last-written reason; ReadLatestAsync(0) throws ArgumentOutOfRangeException
[Fact] public async Task SetReviewedUpdatesOneRowAndReportsAnAbsentId()
// write 2 rows; SetReviewedAsync(firstId, true) → true, RowsAsync() shows only that row Reviewed; SetReviewedAsync(long.MaxValue, true) → false
[Fact] public async Task CountUnreviewedCountsOnlyUntickedRows()
// write 3 rows, tick one → CountUnreviewedAsync() == 2
```

- [ ] **Step 2: Run and see them fail** (methods missing):

Run: `dotnet test tests/Agent.Core.Tests -- --filter-class "Agent.Core.Tests.Learning.LearningLogTests"`

- [ ] **Step 3: Implement the three methods** on `LearningLog` with the signatures above. `SetReviewedAsync` loads with `FindAsync`, sets `Reviewed`, `SaveChangesAsync`; a null find returns `false`.

- [ ] **Step 4: Run the class, then the collection**

Run: `dotnet test tests/Agent.Core.Tests -- --filter-class "Agent.Core.Tests.Learning.LearningLogTests"`
Expected: all pass (or Skipped as a block if SQL Server is not answering; then run with `$env:ZAINCASH_TEST_SQL='1'` to be sure).

- [ ] **Step 5: Commit**

```bash
git add src/Agent.Core/Learning/LearningLog.cs tests/Agent.Core.Tests/Learning/LearningLogTests.cs
git commit -m "feat(core): LearningLog reads its newest rows, counts the unreviewed ones and marks one reviewed (milestone 10)"
```

---

### Task 3: The Learning data panel and the menu count

**Files:**
- Create: `src/Agent.Web/Components/Console/LearningDataPanel.razor`
- Modify: `src/Agent.Web/Components/Pages/Home.razor` (the `LearningData` case), `src/Agent.Web/Components/Layout/MainLayout.razor` (the count on `menu-learning`), `src/Agent.Web/Components/Console/GridPager.razor` (doc comment: grid `learning`)
- Delete: `src/Agent.Web/Components/Console/Placeholder.razor` if nothing else uses it (grep first)
- Modify: `docs/agent-web.md` (panel section, test-id rows, the layout Mermaid diagram at line 35-38, the empty-state table row at line 863, the known-gaps row at line 1198)

**Interfaces:**
- Consumes: Task 2's three methods on `LearningLog`; `PagedList<T>.Of`, `GridPager`, `EmptyState`, `TableSkeleton`; `IOptionsMonitor<AgentOptions>.CurrentValue.LogPageSize`; `IOptionsMonitor<ConsoleOptions>.CurrentValue.GridPageSize`; `ChatSession.Changed` and `.Busy`; `ConsoleNavigation.Changed`.
- Produces: test ids `learning-panel`, `learning-refresh`, `learning-row` (`data-id`, `data-reason`, `data-user`, `data-reviewed` = `true`/`false`), `learning-reviewed` (the checkbox input, `data-id`), `menu-learning-count` (absent when 0); pager `data-grid="learning"`. Column order: At (UTC), User, Reason, Detail, Masked text, Reviewed.

- [ ] **Step 1: Build the panel** following `LogPanel.razor` line for line in structure: title "Learning data", `learning-refresh` button, progress bar on reload, `TableSkeleton` before the first read, `EmptyState` (`Icons.Material.Filled.School`, "No learning rows yet."), `MudSimpleTable` of `PagedList<LearningEntryRow>.Of(rows, page, PageSize)`, `GridPager Grid="learning"`. The read is `scope.ServiceProvider.GetRequiredService<LearningLog>().ReadLatestAsync(Options.CurrentValue.LogPageSize, token)`. Reload on `Session.Changed` when not busy (as `LogPanel`), and on Refresh. The masked-text cell: `<td dir="auto" class="learning-text">@row.MaskedText</td>`. The Reviewed cell: a plain `<input type="checkbox" data-testid="learning-reviewed" data-id="@row.Id" checked="@row.Reviewed" @onchange="e => ToggleAsync(row.Id, (bool)e.Value!)">`. `ToggleAsync` calls `SetReviewedAsync`; on `false` (row gone) or on any exception it sets `failure` and calls `LoadAsync`; on `true` it updates the row in `rows` and raises the count refresh (Step 3).

- [ ] **Step 2: Wire `Home.razor`**: replace the `Placeholder` with `<LearningDataPanel />`. Remove `Placeholder.razor` if unused.

- [ ] **Step 3: The menu count.** In `MainLayout.razor`, after the "Learning data" text, render `<span class="console-menu-count" data-testid="menu-learning-count">@unreviewed</span>` only when `unreviewed > 0`. Add a small scoped-free singleton `LearningCount` in `src/Agent.Web/Services/LearningCount.cs`: `public sealed class LearningCount(IServiceScopeFactory scopes) { public event Action? Changed; public Task<int> ReadAsync(CancellationToken) ; public void NotifyChanged() }` registered as singleton in `Program.cs`. The layout reads it on init, on `Navigation.Changed`, on `ChatSession.Changed` when not busy, and on `LearningCount.Changed`; the panel calls `NotifyChanged()` after a tick. Style `.console-menu-count` in `app.css` as a small pill from the existing tokens (the badge tint).

- [ ] **Step 4: Run the console by hand** (wallet first, then web), say the ADD-02 sentence from the dev set in the phone, open Learning data, tick the row, see the count appear and vanish.

Run: `dotnet run --project src/Wallet.MockApi` and `dotnet run --project src/Agent.Web`; open http://localhost:5089.

- [ ] **Step 5: Docs.** In `docs/agent-web.md`: a "Learning data" section after the Log section (what it reads, the count, the tick, the reset note, a Mermaid sequence diagram tick → `SetReviewedAsync` → `NotifyChanged` → menu); the new rows in the `data-testid` table; the diagram at lines 35-38 and the rows at 863 and 1198 updated; `GridPager`'s grid list gains `learning`.

- [ ] **Step 6: Build and run the web tests**

Run: `dotnet build` then `dotnet test tests/Agent.Core.Tests`
Expected: 0 failed.

- [ ] **Step 7: Commit**

```bash
git add src/Agent.Web docs/agent-web.md
git commit -m "feat(web): the Learning data panel (newest rows, paged, Reviewed tick) and the unreviewed count on the menu item (milestone 10)"
```

---

### Task 4: The report contract and the reader in `Agent.Core.Reports`

**Files:**
- Create: `src/Agent.Core/Reports/ReportRows.cs` (the moved records), `src/Agent.Core/Reports/ReportDocument.cs`, `src/Agent.Core/Reports/ReportReader.cs`
- Modify: `src/Agent.Eval/Grading/ReportWriter.Targets.cs` (remove `TargetRow`, `RepeatTarget`), `src/Agent.Eval/Grading/ReportWriter.cs` (remove `GroupScore`), `src/Agent.Eval/Grading/ReportWriter.Repeat.cs` (remove `RepeatRun`, `RepeatGroup` if defined there), add `using Agent.Core.Reports;` where they are used
- Test: `tests/Agent.Core.Tests/Reports/ReportReaderTests.cs` (new), `tests/Agent.Core.Tests/Eval/ReportWriterTests.cs` (unchanged, must keep passing)

**Interfaces:**
- Produces, namespace `Agent.Core.Reports`:
  - Records moved unchanged in shape: `TargetRow`, `RepeatTarget`, `GroupScore`, `RepeatRun`, `RepeatGroup`.
  - `ReportHeader(string Set, string SetFileSha256, string Parser, int? Runs, DateTime StartedUtc, IReadOnlyDictionary<string,string> Settings)`.
  - `ReportTime(double MedianMs, double P90Ms)`.
  - `ReportDocument(ReportHeader Header, GroupScore Totals, IReadOnlyList<TargetRow> Targets, ReportTime Time, decimal CostUsd)`.
  - `RepeatReportDocument(ReportHeader Header, IReadOnlyList<RepeatGroup> Groups, IReadOnlyList<RepeatRun> Runs, IReadOnlyList<RepeatTarget> Targets)`.
  - `ReportFile(string FileName, ReportDocument? Single, RepeatReportDocument? Repeat)`: exactly one non-null; `Header` property returns whichever.
  - `ReportEntry(string FileName, string Set, string Parser, DateTime StartedUtc, bool IsAggregate)`.
  - `ReportListing(IReadOnlyList<ReportEntry> Entries, int Skipped, string? DefaultFileName)`.
  - `static class ReportReader`:
    - `static string Resolve(string reportsDirectory)`: `Path.IsPathRooted` ? as is : `Path.Combine(RepositoryRoot.Find(), reportsDirectory)`.
    - `static ReportListing List(string directory, string candidate)`: every `dev-*.json` and `heldout-*.json` whose document parses and has a non-empty `targets`; others counted in `Skipped`; a missing directory is an empty listing. Order: `StartedUtc` descending, aggregate before single on a tie, then `FileName` descending (ordinal). `DefaultFileName`: the first entry whose `Parser` ends with `"-" + candidate` (ordinal), else the first entry, else null.
    - `static ReportFile Read(string path)`: throws `JsonException` or `IOException` as they come; `Repeat` when the header has `runs`, `Single` otherwise.
  - JSON options: `PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower`, `PropertyNameCaseInsensitive = true`; unmapped members ignored (the default).

- [ ] **Step 1: Move the records.** Cut `TargetRow` and `RepeatTarget` from `ReportWriter.Targets.cs`, `GroupScore` from `ReportWriter.cs`, `RepeatRun` and `RepeatGroup` from `ReportWriter.Repeat.cs` into `src/Agent.Core/Reports/ReportRows.cs`, namespace `Agent.Core.Reports`, bodies and doc comments unchanged. Add the `using`. Build.

Run: `dotnet build && dotnet test tests/Agent.Core.Tests -- --filter-class "Agent.Core.Tests.Eval.ReportWriterTests"`
Expected: build clean, tests pass unchanged (the JSON is the same text).

- [ ] **Step 2: Write the failing reader tests** in `ReportReaderTests` (no database; `[Collection]` not needed). Fixture directory: a temp directory per test populated by copying named committed files from `docs/reports` (path from `EvalPaths.RepositoryRoot()`):

```csharp
[Fact] public void EveryCommittedReportWithTargetsReads()
// for each docs/reports/{dev,heldout}-*.json: try ReportReader.Read; count those with targets ≥ 1; assert ≥ 100 read, and each has Header.Set in {"dev","heldout"}, 9 target rows, each Verdict in {"pass","miss","reported","n/a"}
[Fact] public void ListSkipsFilesWithoutTargets()
// temp dir with dev-scripted-oracle-2026-10-15-2052.json (no targets) + dev-claude-sonnet-r3-2026-10-29-1058.json → Entries.Count == 1, Skipped == 1
[Fact] public void ListSkipsUnparseableFile()
// temp dir with dev-bad.json containing "{" → Entries empty, Skipped == 1, no throw
[Fact] public void ListOfMissingDirectoryIsEmpty()
// Entries empty, Skipped 0, DefaultFileName null
[Fact] public void AggregateComesBeforeItsFirstRunOnEqualStart()
// dev-claude-sonnet-repeat3-2026-10-29-1049.json + dev-claude-sonnet-r1-2026-10-29-1049.json → Entries[0].FileName is the repeat3
[Fact] public void DefaultIsTheNewestReportOfTheCandidate()
// the two sonnet files + dev-compatible-gemma-r3-2026-11-01-0735.json, candidate "sonnet" → Entries[0] is gemma, DefaultFileName is the sonnet repeat3
[Fact] public void DefaultFallsBackToNewestWhenNoParserMatches()
// same files, candidate "opus" → DefaultFileName == Entries[0].FileName
[Fact] public void ReadOfASingleRunGivesTotalsTargetsTimeAndCost()
// the sonnet r3 file → Single not null, Repeat null, Totals.Cases 102, Totals.Passed 97, Targets.Count 9, Time.MedianMs 2376, CostUsd 0.1769595m, Header.Parser "claude-sonnet"
[Fact] public void ReadOfAnAggregateGivesGroupsRunsAndTargets()
// the sonnet repeat3 file → Repeat not null, Runs.Count 3, Groups[0].Name "All cases", Targets.Count 9, Header.Runs 3
[Fact] public void TheWriterAndTheReaderAgreeOnTheTargets()
// build a RunSummary as ReportWriterTests does, ReportWriter.Write to a temp dir, ReportReader.Read the json → Single.Targets equals ReportWriter.Targets(results) (record equality), Single.Totals equals the writer's Score
```

- [ ] **Step 3: Run and see them fail** (types missing).

Run: `dotnet test tests/Agent.Core.Tests -- --filter-class "Agent.Core.Tests.Reports.ReportReaderTests"`

- [ ] **Step 4: Implement `ReportDocument.cs` and `ReportReader.cs`** with the interfaces above. `List` reads each candidate file with `Read` inside a `try` that catches `JsonException` and `IOException` and counts the skip; a document whose `targets` is empty or missing is a skip too (so the reader must tolerate a missing `targets` property: make it nullable on the records and check in `List`).

- [ ] **Step 5: Run the class and the full core tests**

Run: `dotnet test tests/Agent.Core.Tests`
Expected: 0 failed.

- [ ] **Step 6: Docs.** `docs/agent-eval.md`: one paragraph that the report rows and the reader live in `Agent.Core.Reports`, that the console reads committed reports with it, and that the 27 reports of 15 and 16 Oct 2026 predate the targets table and are not read.

- [ ] **Step 7: Commit**

```bash
git add src/Agent.Core/Reports src/Agent.Eval/Grading tests/Agent.Core.Tests/Reports docs/agent-eval.md
git commit -m "feat(core): Agent.Core.Reports holds the report rows the runner writes and a ReportReader the console reads them with: listing newest first, the candidate's newest report by default, files without a targets table skipped"
```

---

### Task 5: The Results panel

**Files:**
- Create: `src/Agent.Web/Components/Console/ResultsPanel.razor`
- Modify: `src/Agent.Web/Services/ConsoleOptions.cs` (`ReportsDirectory`), `src/Agent.Web/Services/ConsoleNavigation.cs` (`ConsolePanel.Results`), `src/Agent.Web/Components/Layout/MainLayout.razor` (menu item), `src/Agent.Web/Components/Pages/Home.razor` (case), `src/Agent.Web/Components/Console/SettingsPanel.razor` (a read-only row `Console:ReportsDirectory`), `src/Agent.Web/appsettings.json` (`Console:ReportsDirectory`), `src/Agent.Web/wwwroot/app.css` (verdict chips if `StatusBadge` is not reused)
- Test: `tests/Agent.Core.Tests/Web/ConsoleOptionsTests.cs` (exists? if not, create next to the other `Web` tests), `tests/Agent.Core.Tests/Web/RuntimeSettingsTests.cs` (the read-only row is not editable)
- Modify: `docs/agent-web.md` (Results section, test-id rows, settings table)

**Interfaces:**
- Consumes: `ReportReader.Resolve/List/Read`, `ReportFile`, `ReportListing`; `IOptionsMonitor<ConsoleOptions>`; `IOptions<ModelOptions>.Value.Candidate`.
- Produces: `ConsoleOptions.ReportsDirectory` (`[Required]`, default `"docs/reports"`); `ConsolePanel.Results`; test ids `menu-results`, `results-panel`, `results-select` (a native `<select>` whose option values are file names), `results-header` (`data-set`, `data-parser`, `data-started` as `yyyy-MM-dd HH:mm`), `results-target` (`data-measure`, `data-verdict`), `results-totals`, `results-run` (one per run of an aggregate, `data-run`), `results-skipped` (text "n file(s) skipped", only when n > 0), `results-error`, `empty-state` for no reports.

- [ ] **Step 1: Write the failing option tests**:

```csharp
[Fact] public void ReportsDirectoryDefaultsToDocsReports()  // new ConsoleOptions().ReportsDirectory == "docs/reports"
[Fact] public void ReportsDirectoryMustNotBeEmpty()          // Validator.TryValidateObject on { ReportsDirectory = "" } → false
```
and in `RuntimeSettingsTests`, add `"Console:ReportsDirectory"` to whatever theory asserts that a non-editable name is refused by `RuntimeSettings` (follow the pattern of `Console:WalletAdminKey` there).

- [ ] **Step 2: Run and see them fail.**

Run: `dotnet test tests/Agent.Core.Tests -- --filter-class "Agent.Core.Tests.Web.ConsoleOptionsTests"`

- [ ] **Step 3: Add the setting** to `ConsoleOptions` with a doc comment, to `appsettings.json` under `Console`, and as a read-only row in `SettingsPanel.razor` next to `Console:WalletAdminKey` showing the resolved path (`ReportReader.Resolve`).

- [ ] **Step 4: The enum and the menu.** Add `Results` to `ConsolePanel` after `Log` with a doc comment "The latest eval report's targets table (milestone 10)". In `MainLayout.razor` add the `MudNavLink` `menu-results`, icon `Icons.Material.Filled.Assessment`, text "Results", between Log and Aliases. In `Home.razor` add the case.

- [ ] **Step 5: Build the panel.** On init: `listing = ReportReader.List(ReportReader.Resolve(ConsoleSettings.CurrentValue.ReportsDirectory), Model.Value.Candidate ?? string.Empty)`, `selected = listing.DefaultFileName`, then `Open(selected)`. Render: a title "Results", the `results-select` bound to `selected` (options: `"{StartedUtc:yyyy-MM-dd HH:mm}  {Set}  {Parser}{(IsAggregate ? "  (aggregate)" : "")}  {FileName}"`), `results-skipped` when `Skipped > 0`, `EmptyState` (`Icons.Material.Filled.Assessment`, `"No reports in {directory}."`) when `Entries` is empty. For an opened file: the `results-header` line (set, parser, started, prompt version from `Header.Settings["Model:PromptVersion"]` when present, cases from Totals or Groups[0]); the targets table with columns Measure, Target, Value or Min / Median / Max, Verdict as `<span class="badge badge-{verdict}">` with `pass` on the success tint, `miss` on the danger tint, `reported` and `n/a` muted (class suffix `na` for `n/a`; add the three classes to `app.css` next to the existing badge classes); under it `results-totals`: single run "{Passed} of {Cases} passed ({Percent:0.0} %) · cost {CostUsd:0.0000} USD · median {MedianMs/1000:0.00} s"; aggregate: the `All cases` group as "passed min / median / max of cases" and a small table of `results-run` rows (Run, Passed of Cases, Cost USD, Median s). A `Read` that throws shows `results-error` "Could not read {file}: {ex.GetType().Name}" and leaves the select usable. Numbers are formatted with `CultureInfo.InvariantCulture`.

- [ ] **Step 6: Run the console by hand**, open Results: the sonnet repeat3 of 29 Oct opens first; switch to a gemma file; switch to an old scripted file is impossible (skipped, count shown).

- [ ] **Step 7: Docs.** `docs/agent-web.md`: a "Results" section (what is listed, the default rule, the two shapes, the setting, a Mermaid flow list → default → read → table); test-id rows; the settings table gains the read-only row; the layout diagram gains Results.

- [ ] **Step 8: Build and test**

Run: `dotnet build && dotnet test tests/Agent.Core.Tests`
Expected: 0 failed.

- [ ] **Step 9: Commit**

```bash
git add src/Agent.Web tests/Agent.Core.Tests/Web docs/agent-web.md
git commit -m "feat(web): the Results panel shows a committed report's targets table, the configured candidate's newest report first, every report selectable; Console:ReportsDirectory read-only setting (milestone 10)"
```

---

### Task 6: Journeys

**Files:**
- Create: `tests/Agent.Web.Journeys/Fixtures/reports/` holding copies of `dev-claude-sonnet-r1-2026-10-29-1049.json`, `dev-claude-sonnet-repeat3-2026-10-29-1049.json`, `dev-compatible-gemma-r3-2026-11-01-0735.json` and `dev-scripted-oracle-2026-10-15-2052.json` (copy whole; mark them `CopyToOutputDirectory=PreserveNewest` in the csproj)
- Modify: `tests/Agent.Web.Journeys/JourneyHost.cs` (`host.UseSetting("Console:ReportsDirectory", <absolute fixture path>)` in `StartWeb`), `tests/Agent.Web.Journeys/JourneyBase.cs` (`LearningRowsAsync` polls), `tests/Agent.Web.Journeys/ConsoleJourneys.cs` (three journeys)
- Modify: `docs/agent-web.md` (the flaky-journey note at line 1205 resolved; journey count), `README.md` (journey count line)

**Interfaces:**
- Consumes: Task 3 and Task 5 test ids; `DevSet.Say("ADD-02")`; `Host.ResetAsync` (runs before each journey and clears `LearningEntries`).

- [ ] **Step 1: `LearningRowsAsync` polls**: up to 5 seconds, every 100 ms, until the count is at least `minimum` (new optional parameter `int minimum = 0`), then returns. The two journeys named in `docs/agent-web.md` line 1205 pass `minimum: 1`.

- [ ] **Step 2: Write the three journeys** in `ConsoleJourneys` (U1 console, `Host.WebAddress`):

```csharp
[Fact] public async Task LearningRowCanBeTickedReviewedAndTheMenuCountFollows()
// Say ADD-02; Expect menu-learning-count text "1"; click menu-learning; Expect one learning-row with data-reason "recipient_not_found", data-user "1", data-reviewed "false", and its last-but-one td containing the masked text of DevSet.Say("ADD-02") (ArabicDigits/Masker: the message has no digits, so the text as typed); check learning-reviewed; Expect data-reviewed "true" and menu-learning-count count 0; click learning-refresh; still "true"
[Fact] public async Task ResultsOpensOnTheCandidatesNewestReportAndCanSwitch()
// click menu-results; Expect results-select value "dev-claude-sonnet-repeat3-2026-10-29-1049.json"; results-header data-parser "claude-sonnet"; 9 results-target rows, all data-verdict "pass" except the cost row "reported"; 3 results-run rows; results-skipped text starts "1 file"; select the gemma option; Expect results-header data-parser "compatible-gemma" and at least one results-target with data-verdict "miss"; results-totals contains "of 102 passed"
[Fact] public async Task ResultsShowsTheEmptyStateWhenThereAreNoReports()
// only if a second console can point at an empty directory cheaply (the U5 console: add a UseSetting for it); otherwise drop this journey and note it in the docs
```

- [ ] **Step 3: Run the journeys**

Run: `dotnet test tests/Agent.Web.Journeys`
Expected: the new journeys pass; the two polling journeys pass; total count noted for the docs (0 failed; the screenshot helper skipped).

- [ ] **Step 4: Docs.** `docs/agent-web.md`: the three journeys in the journeys table, the fixture directory, the line-1205 note replaced by "fixed in milestone 10: the read polls". README: the journey count sentence.

- [ ] **Step 5: Commit**

```bash
git add tests/Agent.Web.Journeys docs/agent-web.md README.md
git commit -m "test(journeys): the Learning data tick and menu count, the Results panel on a fixture report directory, and a polling learning read for the two single-read journeys (milestone 10)"
```

---

### Task 7: Gaps, failure modes, retention rule, freeze section, build plan

**Files:**
- Modify: `docs/known-gaps.md` (rows 20, 37; a new row), `docs/failure-modes.md` (two rows), `docs/agent-model.md` (the learning log's retention rule), `docs/disclosure.md` (a `## Freeze, milestone 10` section), `docs/build-plan.md` (milestone 10's "Done when" and the "Delivers" list), `docs/architecture.md` (D17: the results component is built; the known-gaps table row 12 → built)

- [ ] **Step 1: known-gaps.** Row 20: Why "built: milestone 10, 3 Nov 2026"; Today: both panels described in one sentence each; Fix: "—"; Source adds `agent-web.md`. Row 37: Today unchanged; Fix: "Rule stated (agent-model.md, learning log): rows live until the agent database is reset by the runner or the journeys; the console's Reset keeps them; a deletion job after a fixed period once `Reviewed` is true is a deployment item, not built because the text is synthetic." New row (next free number): "**The D12 'save as test case' button is not built.** Why: not yet built, deferred by Khalil 3 Nov 2026 to after milestone 12. Today: a learning row is read and ticked in the console; a new dev case is still written by hand with its answer key and its parse. Fix: a draft export to a separate `data/requests.dev.drafts.jsonl` (masked text, user, reason, date, an empty expected block for Khalil), loaded by nothing."
- [ ] **Step 2: failure-modes.** Two rows in the file's table format: "A report file cannot be read" (skipped in the listing and counted; an opened file that fails shows `results-error`; `ReportReaderTests.ListSkipsUnparseableFile`; journey: the Results journey's skipped count) and "A Reviewed tick on a row that a reset removed" (`SetReviewedAsync` returns false, the panel reloads; `LearningLogTests.SetReviewedUpdatesOneRowAndReportsAnAbsentId`).
- [ ] **Step 3: agent-model.md.** Under the learning log: the retention rule exactly as in row 37's Fix.
- [ ] **Step 4: disclosure.** `## Freeze, milestone 10`: the tag `milestone-10`, model `claude-sonnet-5-5` (candidate `sonnet`, provider anthropic), prompt `src/Agent.Core/Parsing/parse-prompt.md` version 6 (22 Oct 2026), seed hash from `data/seed/seed-hash.txt` (read the file, do not type it), dev set SHA-256 from the sonnet report header (`set_file_sha256`), the frozen surface in two lines as the design states it, and a list headed "Fixes after the freeze" that is empty.
- [ ] **Step 5: build-plan.** Milestone 10's "Done when": each line followed by what shows it (the comparison report file; the tag; the disclosure section). Add the three rulings of 3 Nov 2026 (Sonnet, branch per milestone, Reviewed tick with the button deferred) in two sentences under "Delivers".
- [ ] **Step 6: architecture.** D17: "A results component ... optional" becomes "built in milestone 10 (Results panel)". The known-gaps table row 12: "built 3 Nov 2026".
- [ ] **Step 6b: CLAUDE.md and README.** One sentence each on the two panels (Learning data with its Reviewed tick and count; Results with `Console:ReportsDirectory`) in the console paragraphs.
- [ ] **Step 7: Commit**

```bash
git add docs
git commit -m "docs: milestone 10 close-out: known gaps 20 closed, 37 rule stated, D12 button deferred as a draft export; failure modes for an unreadable report and a tick on a reset row; the freeze section in the disclosure; build plan done-when lines"
```

---

### After the tasks (the controller)

1. Full suite from a clean build: `dotnet build` then `dotnet test`; record the counts.
2. Run `dotnet-security-reviewer` on `git diff main...milestone-10`; write `docs/reviews/milestone-10-security-review.md` in the existing format; fix what it finds on the branch; one commit per fix wave with the disposition in the review file.
3. `git checkout main && git merge --no-ff milestone-10`, tag `milestone-10` on the merge commit with a message naming the model, prompt 6 and seed v4, push `main` and the tag.
4. Report to Khalil: which "Done when" lines are true and what shows each; anything unsure.
