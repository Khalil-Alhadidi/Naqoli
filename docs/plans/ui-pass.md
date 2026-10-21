# UI pass on Agent.Web, before milestone 10

**Version 0.4, 21 Oct 2026: the brand is `#673b98` (one digit from Khalil's `#663b98`, at his request) and the tasks start. Version 0.3: Khalil's answers recorded, the timer placement chosen (option B), the status badge on both tables; the page-size default of 10 is the working assumption. Kept uncommitted until the tasks started.** Version 0.1 was the plan for review. Planned by Claude (Fable 5.1) from Khalil's brief of 21 Oct 2026; nothing in it is built. It follows `docs/architecture.md` (D17 layout B, D20 the phone's language and strings, section 6) and `docs/build-plan.md` (milestones 4, 7 and 12). Milestone 12's "finished console in layout B" is this work; everything else in milestones 10 to 12 is untouched.

**Goal.** A console that looks professional and finished in front of judges, with no change in behaviour. Every selector the journeys use stays. The phone stays `dir="rtl"`, the console `dir="ltr"`. No phone string changes (D20). No change under `src/Agent.Core`, `src/Wallet.MockApi` or `data/`.

**How to use this plan.** As `docs/build-plan.md`: one task at a time, in order; a task is finished when every "Done when" line is true and shown by a test or a committed file; one commit per task, Khalil's name only; `dotnet test` (1,418 core tests) and `dotnet test tests/Agent.Web.Journeys` (70 tests: 69 journeys and the host test) pass after every task. Anything not settled here is asked, not decided in code.

## Decisions (Khalil's answers of 21 Oct 2026 to the nine questions)

| # | Question | Khalil's answer | Effect on the plan |
|---|---|---|---|
| 1 | Where the account status shows | "This should be in the Contacts grid", then "keep both", then (on my loose wording "Contacts table"): the Contacts grid, which reads from the Accounts table through the wallet's join | Task 5 puts the badge and the muted row on the Contacts grid of the wallet data component and on the console's Aliases grid. Both grids already receive each row's account status: the wallet's contacts route joins a contact to the account behind its phone and an alias to its target account, and returns `accountStatus` on every row. No table, route or read changes |
| 2 | The page size setting | "Please elaborate"; no further word after the explanation | Explained in the note under this table; tasks 4a to 4c stand as written with the default of 10 as the working assumption until Khalil says otherwise |
| 3 | The two phone lists | `Pager_Previous` الصفحة السابقة, `Pager_Next` الصفحة اللاحقة | Task 4d is in. The two keys get Khalil's words as given (no `TODO-AR`, nothing listed in `AwaitingKhalil`) |
| 4 | The menu leaves the drawer | "Yes" | Task 2 as written |
| 5 | Fonts | "Yes" | Tasks 1 and 7 as written: system stack for the console, Noto Naskh Arabic self-hosted for the phone |
| 6 | The header text | "Agentic AI Challenge Iraq : Pay and Bill Agent" | Task 2: that is the title in the bar and the page title; the "Mock wallet · synthetic data" chip stays beside it |
| 7 | The history read | "OK as long as I don't need to scroll to the end to see the history" | Task 4b: newest first, page 1 after every wallet change, so the latest payment is the first row on screen |
| 8 | The timer on Confirm | Saw the mock of three placements (<https://claude.ai/artifact/Juf6qEHp6LdoVXCbQdvGMA>) and chose option B, "timer in the action block" | Task 7: the countdown sentence and its bar move into the card's action block, directly above Confirm and Cancel, under a rule that closes the fields |
| 9 | Light only | "Keep the light theme, no need for the dark theme yet" | Task 1 as written |

**Parked by Khalil, 21 Oct 2026:** the amount's format on the card and the thousands separator (`PhoneFormat.Number`, the Arabic thousands separator U+066C) may be rethought later. This pass changes neither; the mock showed them as the code renders them today.

**Question 2, elaborated.** One number, `Console:GridPageSize`, says how many rows every console grid shows on one page: contacts, bills, history, log, aliases, and later the learning data. It lives in the console's own `Console` section of `appsettings.json` (next to `WalletAdminKey`), default 10, allowed 5 to 100. It is not an agent setting: nothing in `Agent.Core` reads it, so it never changes a check, a card or a reply. It appears in the settings component under a "Console" heading with its description, unit and range like the agent's limits (task 6), and can be changed there while the console runs (task 4c), the change applying at the next render and forgotten at restart, exactly as the agent's limits behave. Two existing settings stay separate and are described as such: `LogPageSize` (100) is how many of the newest audit rows the log loads from the database, and `HistoryPageSize` (200) is how many history rows the agent, and now the wallet panel, read from the wallet in one call; the grid page size splits whatever was loaded into pages. The default must stay 10 or more because three journeys count every row of U1's alias and order lists against the wallet; U1 has 4 aliases and at most 10 orders after a purchase, so a first page of 10 holds them all. Below 10 those journeys would need to page before counting.

## Inventory

### Components (`src/Agent.Web/Components`)

| Component | Shows | Built with | Touched by task |
|---|---|---|---|
| `Layout/MainLayout.razor` | App bar (title, hamburger), persistent `MudDrawer` 200 px with the five `MudNavLink` items, `MudMainContent` | MudBlazor layout, no theme set (`<MudThemeProvider />` bare) | 1, 2 |
| `Pages/Home.razor` | `.layout-b` (flex): `.console-columns` (`data-testid="console"`, `dir="ltr"`, a two-column grid: middle component, trace column), then `.phone-column` | plain CSS in `wwwroot/app.css` | 2 |
| `Console/WalletDataPanel.razor` | Balance; Contacts, Bills, History (last 20) tables | `MudSimpleTable` ×3, `MudProgressLinear` while loading | 4b, 5, 8 |
| `Console/LogPanel.razor` | The latest `LogPageSize` audit rows, Refresh | `MudSimpleTable`, `MudProgressLinear` | 4b, 8 |
| `Console/AliasesPanel.razor` | The aliases table, Delete per row, the add form, "No aliases." | `MudSimpleTable`, `MudSelect`, plain inputs | 4b, 5, 8 |
| `Console/SettingsPanel.razor` | Kill switch; Agent (editable and read only), Wallet, Model tables | `MudSwitch`, `MudSimpleTable` ×3, plain inputs | 4a, 4c, 6, 8 |
| `Console/LiveTrace.razor` | Trace entries newest first, Clear | `MudPaper`, plain divs, `.zone-*` classes | 3, 8 |
| `Console/FaultPanel.razor` | Wallet fault, model fault, external debit | plain `select`, inputs, `MudButton` | 8 |
| `Console/ResetPanel.razor` | Reset wallet, result and error lines | `MudPaper`, `MudButton` | 8 |
| `Console/Placeholder.razor` | "Arrives in milestone 10" (Learning data) | `MudPaper` | 8 (empty-state look only) |
| `Phone/PhoneFrame.razor` | `.phone-frame` 390 × 780 (`data-testid="phone-frame"`, `dir="rtl"`, `lang="ar"`), header buttons, screen, compose row | `MudRTLProvider`, plain CSS | 7 |
| `Phone/ChatPanel.razor` | Messages, outcome spans, the action buttons (check again, save alias, copy, consent) | plain divs, `MudButton` | 7 |
| `Phone/CardPanel.razor` | The card: title, to, registered, amount, words, fee, total, balance after, first time, warning, timer, PIN, actions | `MudCard`, `MudProgressLinear` | 7 |
| `Phone/QuestionPanel.razor` | Options and the decline button | `MudButton` full width | 7 |
| `Phone/DirectoryPanel.razor`, `Phone/OrdersPanel.razor` | The alias directory and My orders screens | plain divs, `MudSelect`, `CopyButton` | 4d, 7 |
| `Phone/PinField.razor`, `Phone/CopyButton.razor` | PIN input, copy button | plain input, `MudButton` | 7 (styles only) |
| `Layout/ReconnectModal.razor` (+ `.razor.css`) | Blazor's reconnect dialog | template CSS with its own colours | 1 (colours to tokens) |

Styles: `wwwroot/app.css` (335 lines, the only stylesheet of ours; 30 hard-coded colours) and `Components/Layout/ReconnectModal.razor.css` (7 colours). No `style=` attribute and no `<style>` block exists in any `.razor` file. Fonts: one Google Fonts link for Roboto in `App.razor`; Noto Naskh Arabic is named in the phone's stack but loaded from nowhere, so the phone shows whatever Arabic font the machine has. No font file is in `wwwroot` (it holds `app.css` and `favicon.png`). Packages: MudBlazor 9.11.0 and EF Core Design only; QuickGrid is referenced nowhere.

### Grids, with their data source and the seed's rows

| Grid | Component | Data source | Order today | Rows in the seed (U1, the demo user) | After this pass |
|---|---|---|---|---|---|
| Contacts | `WalletDataPanel` | `GET /users/{id}/contacts`, `contacts` (`WalletClient.GetContactsAsync`) | by contact id | 20 (34 in all) | paged, by id (no time); status badge |
| Bills | `WalletDataPanel` | `GET /users/{id}/bills` | by bill id | 4 (11 in all) | paged, by id |
| History | `WalletDataPanel` | `GET /users/{id}/history?take=20` | newest first (wallet: `createdAt` then id) | 50 (86 in all, every user) | reads `HistoryPageSize` rows, paged, newest first |
| Log (audit) | `LogPanel` | agent table `AuditEntries`, `AuditLog.ReadLatestAsync(LogPageSize)` | newest first (`At` then id) | 0 at start; grows with every action | paged, newest first |
| Aliases | `AliasesPanel` | `GET /users/{id}/contacts`, `aliases` (`ChatSession.ListAliasesAsync`) | by alias id | 4 (9 in all) | paged, by id; status badge |
| Settings (4 tables) | `SettingsPanel` | options in memory; `GET /settings` | declaration order | 15 editable, 12 read-only, 5 wallet, 22 model rows | not paged (not data); descriptions added |
| Live trace | `LiveTrace` | `TraceLog.Entries` (per circuit, in memory, no cap) | newest first | 0 at start | not paged (a scrolling list; the known gap "no cap" stays) |
| Learning data | `Placeholder` | none yet (milestone 10 reads `LearningEntries`) | — | 0 at start | the pager component is ready for it |
| Alias directory (phone) | `DirectoryPanel` | as Aliases | by alias id | 4 | paged (decision 3) |
| My orders (phone) | `OrdersPanel` | `GET /users/{id}/orders` + `GET /store/products` (`OrdersReader`) | newest first | ≤ 9 (9 in all) | paged, newest first (decision 3) |

The seed counts come from `CLAUDE.md`'s migrate line, `SeedSetTests`, `SeedApplyTests`, `WalletClientTests` and `ReadEndpointsTests`; nothing under `data/` was opened. U1's order count and the number of closed accounts are not stated outside `data/`; the implementer reads them from the loaded `SeedSet` in a test, never by hand.

### Selectors the journeys depend on

The 69 journeys locate elements by `data-testid` and `data-*` attributes only. There is no locator by text, role, label, CSS class, MudBlazor class, id, colour or style, and no bounding-box assertion, so a restyle that keeps the attributes below and the DOM order noted keeps every journey. The full contract is the table "The `data-testid` contract" in `docs/agent-web.md`; the ids below are the ones actually read by `tests/Agent.Web.Journeys/*.cs`.

- **Direction.** `phone-frame` must keep `dir="rtl"` and `console` must keep `dir="ltr"` (`Journeys.cs:106-107`). `phone-frame` keeps `data-interactive`.
- **Console menu and panels.** `menu-wallet`, `menu-settings`, `menu-log`, `menu-aliases`, `menu-learning`; `wallet-balance` (text contains the ASCII figure), `wallet-history-row[data-reference]` (found by attribute, not by position: `Journeys.cs:98`, `:149`); `log-row[data-event]` and its eighth `td` (`ConsoleJourneys.cs:35`, the Amount column: column order of the log table is fixed); `log-refresh`; `setting-row[data-name]`, `setting-value`, `setting-input`, `setting-save`, `setting-reset`, `setting-error` (all with `data-name`); `settings-fault-pointer`; `kill-switch[data-on]`; `aliases-panel`, `console-alias[data-alias-id]`, `console-alias-delete`, `console-alias-text`, `console-alias-target`, `console-alias-target-option[data-code]`, `console-alias-phone`, `console-alias-save`, `console-alias-result[data-result]`.
- **Trace, faults, reset.** `trace-entry[data-zone]` (`user`, `model`, `code`, `write`; `.First` and text filters `lookup_by_key`, `settle_outcome:done`); `fault-panel`, `fault-clear`, `fault-error`, `fault-wallet-mode` (a `select`), `fault-wallet-delay`, `fault-wallet-once` (a checkbox), `fault-wallet-apply`, `fault-wallet-state`, `fault-model-mode`, `fault-model-once`, `fault-model-apply`, `fault-model-state`, `fault-debit-amount`, `fault-debit-apply`, `fault-debit-result`; `reset-panel`, `reset-wallet`, `reset-result`, `reset-error`.
- **Phone.** `phone-input` (filled, Enter pressed, enabled or disabled), `phone-send` (disabled), `chat-message[data-role][data-kind]`, `outcome[data-outcome]`, `card[data-state]`, `card-display`, `card-product-type`, `card-registered`, `card-amount`, `card-amount-words`, `card-fee`, `card-total`, `card-balance-after`, `card-first-time`, `card-warning`, `card-timer`, `card-pin`, `card-confirm` (enabled then disabled), `card-cancel`, `card-check-again`, `card-checking` (asserted absent), `card-try-again`, `card-send-again`, `question-option[data-value]`, `option-last-sent`, `question-decline[data-value]`, `consent-button`, `consent-decline`, `check-again`, `save-alias`, `purchase-code-copy[data-copied]`, `phone-directory`, `phone-directory-screen`, `phone-directory-back`, `directory-alias[data-alias-id]`, `directory-delete`, `directory-empty`, `directory-text`, `directory-target`, `directory-target-option[data-code]`, `directory-phone`, `directory-save`, `directory-result[data-key]`, `phone-orders`, `phone-orders-screen`, `phone-orders-back`, `order-row[data-order-id]`, `order-product`, `order-date`, `order-code`, `order-copy[data-copied]`, `orders-empty`.
- **DOM order the journeys rely on.** Chat messages in time order (`Nth`, `First`, `Last` on `chat-message` and `outcome`); question options in the order the agent gives them (`PurchaseJourneys.cs:155`, `BillJourneys.cs:113`); `order-row.First` is the newest order (`PurchaseJourneys.cs:213`); `trace-entry.First` per zone. Row counts compared with the wallet: `directory-alias`, `console-alias`, `order-row` (question 2).
- **Settings journey.** `AChangedCapTakesEffectWithoutARestart` fills `setting-input[data-name=PerTransactionCapIqd]`, clicks `setting-save`, reads `setting-value`, clicks `setting-reset`, and reads `setting-row[data-name='Wallet:PinRequiredAboveIqd']`. The editable row's input stays a text input.

## Design tokens

All tokens live in one `:root` block at the top of `wwwroot/app.css`. No component rule names a colour, size, radius or shadow by value; every rule uses a token. MudBlazor's palette is set from the same values in `Services/ConsoleTheme.cs` (a `MudTheme`), and a test pins the two equal (task 1), so there is one value per role and one place to change it. Contrast ratios are WCAG 2.x relative-luminance ratios, computed on 21 Oct 2026 (`contrast.py`, scratch; the test of task 1 recomputes them). AA for normal text is 4.5:1, for large text 3:1.

### Brand (Khalil, 21 Oct 2026: `#663b98`, then one digit away at his request: `#673b98`)

| Token | Value | Role | Pairing checked | Ratio |
|---|---|---|---|---|
| `--color-brand` | `#673b98` | Primary and accent: filled buttons, the app bar, links, the active menu item's bar, the phone's send button and progress bar, focus ring | white text on it | 7.91 (AA, AAA) |
| | | | as text on white | 7.91 |
| `--color-brand-hover` | `#5b3486` (brand + 12 % black) | Hover of a filled brand button | white text on it | 9.21 |
| `--color-brand-pressed` | `#4e2d74` (brand + 24 % black) | Pressed or active state | white text on it | 10.76 |
| `--color-brand-tint` | `#f0ebf5` (brand + 90 % white) | Selected menu item, the user's chat bubble, the badge background of brand-coloured chips | brand text on it | 6.74 |
| | | | body text on it | 13.47 |
| `--color-on-brand` | `#ffffff` | Text and icons on brand | | as above |

### Neutrals

| Token | Value | Role | Pairing checked | Ratio |
|---|---|---|---|---|
| `--color-page` | `#f6f7f9` | The page behind the panels | body text on it | 14.74 |
| `--color-surface` | `#ffffff` | Panels, cards, the phone's agent bubble | | |
| `--color-text` | `#1f2328` | Body text | on white | 15.80 |
| `--color-text-muted` | `#59636e` | Secondary text: trace time and detail, units, captions, fee and balance after on the card, input borders | on white | 6.11 |
| `--color-border` | `#d0d7de` | Dividers, table rules, panel edges (decorative, not text) | | |
| `--color-phone-bezel` | `#212121` | The phone's bezel | | |

### Semantic

| Token | Value | Role | Pairing checked | Ratio |
|---|---|---|---|---|
| `--color-success` | `#1a7f37` | `done` and `active` text, success icons | on white | 5.08 |
| `--color-success-tint` | `#dafbe1` | Badge background for `done`, `active` | success text on it | 4.56 |
| `--color-warning` | `#9a6700` | Warning text: the card's first-time and repeat lines, `executing` | on white | 4.87 |
| `--color-warning-tint` | `#fff8c5` | The warning band's background | warning text on it | 4.52 |
| `--color-danger` | `#b42318` | Error text, Delete and Reset and Clear all buttons, `failed`, `closed` text is **not** this (see badges) | on white | 6.57 |
| `--color-danger-tint` | `#ffebe9` | Badge background for `failed`, error lines' background | danger text on it | 5.73 |
| `--color-neutral-tint` | `#eaeef2` | Badge background for `closed` and other muted states | muted text on it | 5.24 |

### Zones (the trace, its legend, and anywhere a zone is named)

No zone is purple; each is paired with a Material icon and its label, so the zones read without colour. The colour is used the same way everywhere: a 4 px left bar on the entry, the icon and the label in the colour, the legend chip in the colour.

| Token | Value | Zone | Icon (`Icons.Material.Filled`) | Text on white | Against brand `#673b98` |
|---|---|---|---|---|---|
| `--zone-user` | `#1565c0` (blue) | User | `Person` | 5.75 | different hue (blue vs purple) |
| `--zone-model` | `#b45309` (amber) | Model | `AutoAwesome` | 5.02 | different hue |
| `--zone-code` | `#2e7d32` (green) | Code | `Code` | 5.13 | different hue |
| `--zone-write` | `#b42318` (red, the danger value: money leaves) | Write | `Payments` | 6.57 | different hue |

### Spacing, type, radii, shadows, focus

| Token | Value | Role |
|---|---|---|
| `--space-1` to `--space-8` | 4, 8, 12, 16, 20, 24, 32, 40 px | The only spacing values used in our CSS |
| `--text-xs`, `--text-sm`, `--text-md`, `--text-lg`, `--text-xl`, `--text-2xl` | 12, 13, 14, 16, 20, 28 px | Trace and captions; table cells; body; phone body; panel titles; the card's amount |
| `--font-console` | `system-ui, "Segoe UI", Roboto, Helvetica, Arial, sans-serif` | Everything outside the phone (also MudBlazor's typography) |
| `--font-phone` | `"Noto Naskh Arabic", "Segoe UI", Tahoma, sans-serif` | Inside the phone; the first font self-hosted (decision 5) |
| `--font-mono` | `Consolas, "Cascadia Mono", monospace` | The trace |
| `--radius-sm`, `--radius-md`, `--radius-lg`, `--radius-phone` | 4, 8, 14, 36 px | Badges and inputs; panels and buttons; bubbles and the card; the phone |
| `--shadow-1`, `--shadow-2` | `0 1px 2px rgba(31,35,40,.08)`, `0 4px 12px rgba(31,35,40,.12)` | Panels; the card and the phone |
| `--focus-ring` | `2px solid var(--color-brand)`, offset 2 px | Every `:focus-visible` |

### Placeholders Khalil supplies or confirms

| Item | Proposed | Status |
|---|---|---|
| Brand colour | `#673b98` | Khalil gave `#663b98` on 21 Oct 2026 and asked for one digit changed, purple and close; `#673b98` is that |
| Arabic font | Noto Naskh Arabic, regular and bold | confirmed 21 Oct 2026 |
| Header title and chip | "Agentic AI Challenge Iraq : Pay and Bill Agent"; chip "Mock wallet · synthetic data" | given 21 Oct 2026 |
| Logo or wordmark | none | until the organisers allow one |
| Zone colours | the four above | confirm |
| Grid page size default | 10 | explanation given 21 Oct 2026; awaiting Khalil |
| `Pager_Previous`, `Pager_Next` (phone, spoken labels only) | Khalil's words, 21 Oct 2026 | given |
| Status bar content | the time in Arabic-Indic digits, signal, Wi-Fi and battery icons; no text | confirm |

## MudBlazor palette mapping (task 1)

`ConsoleTheme.Light` is a `MudTheme` whose `PaletteLight` takes `Primary` = brand, `PrimaryDarken` = brand-hover, `PrimaryLighten` = brand-tint, `Error` = danger, `Warning` = warning, `Success` = success, `Info` = zone-user, `TextPrimary` = text, `TextSecondary` = text-muted, `Background` = page, `Surface` = surface, `LinesDefault` = border, `AppbarBackground` = brand, `AppbarText` = on-brand, `DrawerBackground` = surface; `Typography.Default.FontFamily` = the console stack. `MainLayout.razor` passes it as `<MudThemeProvider Theme="ConsoleTheme.Light" />`. `Color.Primary`, `Color.Error` and `Color.Warning` on existing components then carry the tokens with no markup change.

## Pagination: why not QuickGrid, and what instead

QuickGrid (`Microsoft.AspNetCore.Components.QuickGrid`) renders its own `<tr>` and gives no way to put `data-testid="wallet-history-row" data-reference=…`, `data-testid="log-row" data-event=…` or `data-alias-id` on a row; the journeys find rows by those attributes. It would also be a new runtime dependency. `MudTable` with `MudTablePager` could keep the row attributes through `MudTr`, but swaps every table's markup for a template model. The simplest option that is the same on every grid: the tables stay `MudSimpleTable` with their `<tr>` attributes exactly as they are, and each binds to one page of its list through a tiny shared helper and one shared pager component (task 4a). Lists with a time are shown newest first; lists without one keep the wallet's order by id.

```text
+----------------------------------------------------------------------------------------+
| bar: title · "Mock wallet · synthetic data" chip                                        |
+----------+--------------------------------------+--------------------+-----------------+
| menu     | main (the selected component)        | trace column       | phone           |
| 200 px   | minmax(0, 3fr)                       | minmax(320px, 2fr) | 390 px          |
|          |                                      | Reset, Faults,     | sticky, top     |
|          |                                      | Live trace (fills  |                 |
|          |                                      | the rest, scrolls) |                 |
+----------+--------------------------------------+--------------------+-----------------+
grid-template-areas: "bar bar bar bar" / "menu main trace phone"; the `console` element
(dir="ltr") wraps main and trace as a sub-grid with areas "main trace".
```

## Tasks

Each task is one commit. "Files" names what changes; nothing outside `src/Agent.Web`, `tests/Agent.Core.Tests/Web`, `tests/Agent.Web.Journeys` and `docs/` is touched. Tests of the web project live in `tests/Agent.Core.Tests/Web` (the project already references `Agent.Web`), and a test that reads `wwwroot/app.css` finds it from the solution root the way the existing tests find the development set.

### Task 1: Design tokens, the MudBlazor theme, and no hard-coded colours

**Files.** Modify `src/Agent.Web/wwwroot/app.css` (the `:root` block; every colour, spacing, size, radius and shadow in the file replaced by a token; the Blazor error UI and error boundary included), `src/Agent.Web/Components/Layout/ReconnectModal.razor.css` (its seven colours to tokens; the stray blank line between `&[open]` and `{` closed up), `src/Agent.Web/Components/App.razor` (the Google Fonts link removed), `src/Agent.Web/Components/Layout/MainLayout.razor` (`Theme="ConsoleTheme.Light"`). Create `src/Agent.Web/Services/ConsoleTheme.cs`. Test: `tests/Agent.Core.Tests/Web/DesignTokensTests.cs`. Docs: `docs/agent-web.md` gets a "Design tokens" section with the token table above.

**Delivers**

- The token block of the table above, one `:root`, light only (decision 9).
- `ConsoleTheme.Light`, the one `MudTheme`, with the palette mapping above and the console font stack as typography.
- Nothing visible changes yet except the brand colour on the buttons, the app bar and the progress bars, and the console font. The zone colours are still the old four (task 3 changes them); they move into tokens now.

**Done when**

- `DesignTokensTests.EveryColourInTheStylesheetIsAToken`: no hex, `rgb(`, `hsl(` or named colour literal appears in `app.css` or any `.razor.css` outside the `:root` block, and none in any `.razor` file.
- `DesignTokensTests.EveryTextPairingMeetsAA`: the test parses the `:root` block and computes the ratios of the pairs in the token table (white on brand, brand-hover, brand-pressed; brand on tint; text and muted on white and page; success, warning, danger and the four zones on white; each badge text on its tint). Each is 4.5 or more.
- `DesignTokensTests.TheMudThemeUsesTheTokens`: every palette value of `ConsoleTheme.Light` equals the token it maps to in the table above, read from `app.css`.
- `DesignTokensTests.NoFontIsLoadedFromTheNetwork`: `App.razor` has no `<link>` to a host other than the app's own.
- All tests and the 70 journey tests pass.

### Task 2: Layout B on one CSS grid

**Files.** Modify `src/Agent.Web/Components/Layout/MainLayout.razor` (header row with the title and the chip; the menu as a `MudNavMenu` in a `<nav class="console-menu">`, the five `MudNavLink` with their `data-testid` unchanged; no `MudLayout`, `MudAppBar`, `MudDrawer`; decision 4), `src/Agent.Web/Components/Pages/Home.razor` (the `console` element wraps main and trace as today; the phone column), `wwwroot/app.css` (the grid, the areas, the trace column's height). Docs: `docs/agent-web.md` "Layout B" (the Mermaid diagram and the text), `docs/architecture.md` D17 gets one line "built on a CSS grid with named areas, 21 Oct 2026", and the known-gap row about the trace column's height is closed.

**Delivers**

- The grid in the sketch above: `bar` across the top, then `menu | main | trace | phone`. The menu is 200 px, the phone 390 px, the two middle columns share the rest.
- The active menu item: brand bar on its left, brand-tint background, bold label; icons unchanged.
- The bar's title is "Agentic AI Challenge Iraq : Pay and Bill Agent" (Khalil, 21 Oct 2026; also the `PageTitle`), with the chip "Mock wallet · synthetic data" beside it, always on screen, brand-tint background with brand text.
- The trace column is `height: calc(100vh - bar height)` with the trace list filling what Reset and Faults leave and scrolling inside, so the column never grows past the window (the known gap in `docs/agent-web.md`).
- Below 1,280 px the phone column drops under the console (a `@media` rule); the demo laptop is wider, so this is a safety net, not a design target.

**Done when**

- `Home.razor` or `MainLayout.razor` declares `grid-template-areas` with exactly `bar`, `menu`, `main`, `trace`, `phone`, and the `console` element keeps `dir="ltr"` while wrapping `main` and `trace`.
- The chip's text is visible in the page (a journey assertion added to `HammoudiHappyPath`: `GetByTestId("mock-label")` has text "Mock wallet · synthetic data"; a new id, additive).
- `HammoudiHappyPath` still finds `phone-frame` `rtl` and `console` `ltr`; all 70 journey tests pass.
- The page shows no horizontal scrollbar at 1,440 × 900 and at 1,920 × 1,080 (checked by hand and noted in the commit message; Playwright's default viewport of 1,280 × 720 runs the suite).

### Task 3: Zone colours and a legend

**Files.** Modify `wwwroot/app.css` (`.trace-entry.zone-*` use the zone tokens; icon and label styles), `src/Agent.Web/Components/Console/LiveTrace.razor` (an icon per entry before the label; a legend row under the title with the four chips: icon, label; `Trace.Entries` and the `data-zone` values unchanged). Docs: `docs/agent-web.md` "The live trace" names the four colours and icons; `docs/failure-modes.md` unchanged.

**Delivers**

- User blue, Model amber, Code green, Write red, as the token table says; the old purple Model goes (brand is purple).
- Every entry: the 4 px bar, the icon and the label in the zone's colour, the step in body text, the detail muted. The label text stays the enum name, as today.
- A legend with the same four pairings, so the zones read without colour.

**Done when**

- `DesignTokensTests.NoZoneIsPurpleAndEachDiffersFromTheBrand`: the four zone tokens have a hue at least 40° from `#673b98` and from each other (hue computed from the hex).
- `HammoudiHappyPath`'s zone assertion passes unchanged (`trace-entry[data-zone=…]` for the four zones).
- The legend's four items carry `data-testid="trace-legend"` and `data-zone` (additive) and a journey assertion in `HammoudiHappyPath` finds four of them.
- All tests and the 70 journey tests pass.

### Task 4a: The page size setting and the shared pager

**Files.** Modify `src/Agent.Web/Services/ConsoleOptions.cs` (`GridPageSize`, `int`, `[Range(5, 100)]`, default 10; decision 2, explanation given), `src/Agent.Web/appsettings.json` (`"Console": { "WalletAdminKey": "dev", "GridPageSize": 10 }`), `src/Agent.Web/Components/Console/SettingsPanel.razor` (a "Console" heading with the read-only row `Console:GridPageSize`, `setting-row` with `data-name`). Create `src/Agent.Web/Services/Paging.cs` (`PagedList<T>`: `static PagedList<T> Of(IReadOnlyList<T> rows, int page, int pageSize)` returning `Rows`, `Page`, `PageCount`, `Total`, `First`, `Last`; a page beyond the end clamps to the last page; an empty list is one empty page), `src/Agent.Web/Components/Console/GridPager.razor` (parameters `Total`, `Page`, `PageSize`, `PageChanged`; renders "1–10 of 42", Previous and Next as `MudIconButton` with `aria-label`, disabled at the ends; `data-testid="grid-pager"` with `data-page` and `data-pages`; hidden when `Total <= PageSize`). Tests: `tests/Agent.Core.Tests/Web/PagedListTests.cs`, `ConsoleOptionsTests` extended.

**Delivers**

- One setting, one helper, one pager, used by every console grid in 4b.
- The pager is English, left to right, console only.

**Done when**

- `PagedListTests`: `ASeedSizedListPagesByTen` (42 rows: 5 pages, the last with 2), `APageBeyondTheEndClampsToTheLast`, `AnEmptyListIsOneEmptyPage`, `AShrunkListAfterADeleteKeepsAValidPage`.
- `ConsoleOptionsTests.GridPageSizeIsBoundedFiveToOneHundred` (4 and 101 refused at start, 5 and 100 accepted; default 10).
- `setting-row[data-name='Console:GridPageSize']` shows 10 (an assertion added to `AChangedCapTakesEffectWithoutARestart`, which already reads a settings row).
- All tests and the 70 journey tests pass.

### Task 4b: Every console grid paged, newest first

**Files.** Modify `src/Agent.Web/Components/Console/WalletDataPanel.razor` (contacts, bills and history each hold a page index; history reads `Options.Value.HistoryPageSize` rows, decision 7; the constant `HistoryRows` goes; the heading "History (last 20)" becomes "History"), `LogPanel.razor` (a page over the `LogPageSize` rows it loads; the heading "Log (latest 100)" becomes "Log" with the total in the pager), `AliasesPanel.razor` (a page over the aliases). A reload keeps the current page when it still exists and clamps otherwise (`PagedList.Of` does this), except the history and the log, which return to page 1 on a wallet change so the newest row is on screen. Docs: `docs/agent-web.md` sections for the three panels; the settings description of `LogPageSize` (task 6) says it is the number of rows loaded, not the page.

**Delivers**

- Contacts (20 rows for U1, 2 pages at 10), bills (4, no pager shown), history (50 for U1 plus the demo's, 5 pages or more), the log (grows with the demo; pages at 10 over the latest 100), aliases (4, no pager shown) all through `GridPager`.
- History and log newest first, page 1 after every change, so the row a journey looks for is on the first page.

**Done when**

- `HammoudiHappyPath`, `DoubleTapExecutesOnce`, `TypedNumberShowsTheRegisteredNameAndFirstTime` and every journey that asserts `wallet-history-row[data-reference]` or `wallet-balance` pass unchanged: the new row is on page 1.
- `BalanceAnswersAfterConsentAndTheLogKeepsNoFigure` and `KillSwitchStopsEverything` pass unchanged: the `log-row[data-event]` rows they read are on page 1 (the log holds fewer than 10 rows in those journeys; the assertion that counts two `kill_switch` rows is on page 1 too).
- `TheConsoleAliasesPanelListsAddsAndDeletes` and `AnAliasSavedOnThePhoneShowsInTheConsoleDirectory` pass unchanged: 5 aliases fit one page of 10.
- A new journey `TheHistoryGridPagesNewestFirst` (U1): after one Hammoudi payment the wallet panel's `grid-pager[data-page=1]` shows, page 1 holds the new reference, and the Next button shows a page whose rows are all older (`wallet-history-row` references not equal to the new one).
- `WalletDataPanel.razor` contains no numeric literal for a row count.
- All tests and the 71 journey tests pass.

### Task 4c: The page size editable live (decision 2)

**Files.** Modify `src/Agent.Web/Services/RuntimeSettings.cs` (a second list `EditableConsoleNames` = `[nameof(ConsoleOptions.GridPageSize)]`; `TrySet` and `Reset` accept `Console:GridPageSize`; the candidate for a `Console:` key binds a fresh `ConsoleOptions` and runs the registered `IValidateOptions<ConsoleOptions>`; `AddRuntimeSettings` passes those validators), `SettingsPanel.razor` (the Console row becomes editable: `setting-value`, `setting-input`, `setting-save`, `setting-reset`, `setting-error`, all with `data-name="Console:GridPageSize"`), the three paged panels read `IOptionsMonitor<ConsoleOptions>.CurrentValue.GridPageSize` on each render. Tests: `RuntimeSettingsTests` extended.

**Delivers**

- A saved page size applies to the next render of every grid, and is forgotten at restart, like the agent's limits.

**Done when**

- `RuntimeSettingsTests.GridPageSizeIsEditableWithinItsRange` (5 and 100 accepted, 4 and 101 refused, the message names `Console:GridPageSize`), `RuntimeSettingsTests.AKeyThatIsNotEditableIsRefused` still refuses `Console:WalletAdminKey`.
- The journey `TheHistoryGridPagesNewestFirst` gains a step: save 5 under `Console:GridPageSize`, the pager shows more pages and the new reference is still on page 1; `setting-reset` restores 10.
- All tests and the 71 journey tests pass.

### Task 4d: The two phone lists paged (decision 3)

**Files.** Create `src/Agent.Web/Components/Phone/PhonePager.razor` (chevron icons only, `aria-label` from `Pager_Previous` and `Pager_Next`, the indicator "page of pages" as two numbers through `PhoneFormat.Number`, with the digits separated by the existing `List_joiner` pattern or a plain space; `data-testid="phone-pager"`, `data-page`, `data-pages`; hidden when one page). Modify `DirectoryPanel.razor` and `OrdersPanel.razor` (a page over their lists; orders newest first as the wallet gives them), `src/Agent.Web/Resources/PhoneStrings.resx` (two keys with Khalil's words of 21 Oct 2026, copied from the decisions table: `Pager_Previous` الصفحة السابقة, `Pager_Next` الصفحة اللاحقة; `AwaitingKhalil` stays empty).

**Done when**

- `MyOrdersListsThePurchaseCodesNewestFirstAndCopies`, `ThePhoneDirectoryListsAddsAndDeletes` and the three alias-refusal journeys pass unchanged (every row of U1 fits one page of 10).
- `PhoneStringsTests.NoValueIsAPlaceholderExceptTheKeysAwaitingKhalil` passes with `AwaitingKhalil` still empty (the two values are Khalil's, not placeholders).
- All tests and the journey tests pass.

### Task 5: Status badges (account status, display only)

**Files.** Create `src/Agent.Web/Components/Console/StatusBadge.razor` (parameter `Value`; a `span.badge` with `data-testid="status-badge"`, `data-status`, the lower-case value as its text and an icon: `active` `CheckCircle` in success on success-tint, `closed` `Block` in muted on neutral-tint; any other value muted on neutral-tint with no icon, so a value the wallet adds later still shows). Modify `WalletDataPanel.razor` (the Contacts grid gains a Status column, last; a `closed` row gets `class="row-muted"`), `AliasesPanel.razor` (the same column before Delete; the same muted row; Khalil, 21 Oct 2026: "keep both"), `wwwroot/app.css` (`.badge`, `.row-muted`: muted text, no change to the row's attributes). Docs: `docs/agent-web.md` for the two panels; decision 1.

**Delivers**

- The distinct account status values in the seed are `active` and `closed` (the seed loader accepts nothing else; `docs/wallet-api.md`). One badge style per value, as above.
- The seed's closed accounts are behind at least two numbers (`07805550120`, contact S20 of U1, and `07805550193`); the exact count is read in the test below from the loaded seed, not typed.
- Nothing about how the agent treats a closed account changes; the badge reads the `AccountStatus` string the panel already receives.

**Done when**

- A new journey `AClosedContactShowsItsStatusInTheWalletData`: on U1's wallet data, the Contacts row of S20 (found by its code cell) holds `status-badge[data-status=closed]`, S01's holds `status-badge[data-status=active]`, and the count of `status-badge[data-status=closed]` equals the number of U1's contacts whose `AccountStatus` is `closed` as the wallet reports it (`host` client, never a literal).
- `TheConsoleAliasesPanelListsAddsAndDeletes` passes unchanged (the new column adds a cell, the row's `data-alias-id` stays).
- `DesignTokensTests.EveryTextPairingMeetsAA` covers the two badge pairings (already in the table).
- All tests and the journey tests pass.

### Task 6: Settings descriptions

**Files.** Create `src/Agent.Web/Services/SettingDescriptions.cs` (`sealed record SettingDescription(string Name, string Label, string Description, string Unit, long Min, long Max, long Default)`; `static IReadOnlyDictionary<string, SettingDescription> Editable`, one entry per `RuntimeSettings.EditableNames` plus `Console:GridPageSize`). Modify `SettingsPanel.razor` (each editable row shows the label in bold, the description under it in muted text, then "Unit · Range min to max · Default d"; the input gets `aria-describedby`; the `data-testid` and `data-name` attributes unchanged). Tests: `tests/Agent.Core.Tests/Web/SettingDescriptionsTests.cs`. Docs: `docs/agent-web.md` "The settings component" shows the table below.

**Delivers.** The English text below, for Khalil's review; ranges and defaults are those of `AgentOptions` (code) and `ConsoleOptions`.

| Setting | Label | Description (one line) | Unit | Range | Default |
|---|---|---|---|---|---|
| `MinAmountIqd` | Minimum amount | The smallest payment the agent will prepare a card for. | IQD | 1 to 1,000,000,000,000 | 1,000 |
| `PerTransactionCapIqd` | Per-payment cap | The largest amount of one payment, before the fee. | IQD | 1 to 1,000,000,000,000 | 5,000,000 |
| `PerDayCapIqd` | Daily cap | The most a user may pay in one Iraq day, fees excluded. | IQD | 1 to 1,000,000,000,000 | 10,000,000 |
| `CardTimerSeconds` | Card timer | How long a confirmation card stays open before it expires. | seconds | 1 to 3,600 | 45 |
| `MaxProductsInOneQuestion` | Products per question | The most store products one question offers to tap. | count | 1 to 50 | 8 |
| `RepeatWarningWindowMinutes` | Repeat warning window | A payment like one made within this window carries a repeat warning on its card. | minutes | 0 to 1,440 | 10 |
| `HistoryDays` | History window | How far back the agent reads payments for the daily cap and the repeat warning. | days | 1 to 3,650 | 90 |
| `MinSubstringWordLength` | Shortest partial word | A typed word shorter than this must equal a whole word of a saved name; longer words may match inside one. | characters | 1 to 20 | 3 |
| `MaxYesNoWords` | Yes or no word limit | A message of only yes or no words counts as a yes or a no when it has at most this many words. | words | 1 to 20 | 4 |
| `LogPageSize` | Log rows loaded | How many of the newest audit rows the log loads; the grid page size splits them into pages. | rows | 1 to 1,000 | 100 |
| `CheckingRetrySeconds` | Checking retry | While a card is checking, the phone asks the wallet again every this many seconds. | seconds | 1 to 600 | 5 |
| `MaxAliasLength` | Alias length | The longest alias text that can be saved. | characters | 1 to 100 | 100 |
| `MaxMessageChars` | Message length | The longest typed message the agent reads; a longer one is refused as not understood. | characters | 1 to 32,000 | 500 |
| `MaxRequestsPerMessage` | Requests per message | The most requests of one message that get cards; the rest are named in the reply. | count | 1 to 20 | 3 |
| `MaxNamedNotPrepared` | Requests named | The most of the leftover requests the reply names. | count | 1 to 20 | 5 |
| `Console:GridPageSize` | Grid page size | How many rows each console grid shows per page. | rows | 5 to 100 | 10 |

**Done when**

- `SettingDescriptionsTests.EveryEditableSettingHasADescription`: every name in `RuntimeSettings.EditableNames` (and `GridPageSize`) has an entry, and no entry names a setting that is not editable.
- `SettingDescriptionsTests.EveryRangeAndDefaultMatchesTheOptions`: each entry's `Min`, `Max` and `Default` equal the `[Range]` attribute and the property's initial value on `AgentOptions` (or `ConsoleOptions`), read by reflection, so the table cannot drift from the code.
- `SettingDescriptionsTests.EveryDescriptionIsOneSentence`: each description ends with one full stop and has none inside.
- `AChangedCapTakesEffectWithoutARestart` passes unchanged.
- All tests and the journey tests pass.

### Task 7: Phone polish

**Files.** Create `src/Agent.Web/Components/Phone/PhoneStatusBar.razor` (the time, `PhoneFormat.Digits(now.ToString("HH:mm"))` from `TimeProvider`, refreshed by a one-minute timer disposed with the component; `MudIcon` signal, Wi-Fi, battery; brand background, on-brand text; no string), `src/Agent.Web/wwwroot/fonts/NotoNaskhArabic-Regular.woff2`, `NotoNaskhArabic-Bold.woff2`, `OFL.txt` (decision 5). Modify `PhoneFrame.razor` (the status bar above the header), `CardPanel.razor` (decision 8, option B: the timer line and its bar move into the action block, directly above Confirm and Cancel, under a rule that closes the fields, as the mock of 21 Oct 2026 shows; the first-time note's class becomes `phone-card-band`; the `data-testid` values and the strings unchanged), `wwwroot/app.css` (`@font-face`; the frame 390 px outer with an 8 px bezel at `--radius-phone` and `--shadow-2`; `.phone-card-amount` at `--text-2xl` bold; recipient at `--text-lg`; fee and balance after muted; `.phone-card-band` warning text on warning tint with an icon, full width; the repeat warning the same band; the user bubble on brand tint, the agent bubble on surface with a border; the compose row's send button brand). Docs: `docs/agent-web.md` "The phone" and `docs/disclosure.md` (one line: Noto Naskh Arabic, version, SIL OFL 1.1, self-hosted under `wwwroot/fonts`).

**Delivers**

- A phone of about 390 px with a status bar, one Arabic font that loads offline, the card hierarchy of the brief, the timer beside Confirm, the first-time line as a warning band. The same fields, the same strings, the same ids.

**Done when**

- `HammoudiHappyPath`, `TypedNumberShowsTheRegisteredNameAndFirstTime` (`card-first-time`), `TimerKillsTheButton` (`card-timer`, `card-confirm` disabled), `SendAgainAfterDone` (`card-warning`) and `PinFieldShowsForALargeAmountAndThePinIsNeverLogged` pass unchanged.
- `DesignTokensTests.NoFontIsLoadedFromTheNetwork` still passes and `DesignTokensTests.ThePhoneFontIsSelfHosted`: `app.css` has an `@font-face` for the first family of `--font-phone` whose `src` is under `fonts/`, and the two files exist.
- `PhoneStringsTests` passes with no new key (the status bar has no text).
- A new journey `ThePhoneHasAStatusBarAndTheCardShowsTheTimerByConfirm`: `phone-status-bar` (new id) is visible inside `phone-frame`; after CLN-01, `card-timer` and `card-confirm` share one parent element (`evaluate` on the DOM).
- All tests and the journey tests pass.

### Task 8: General polish

**Files.** Modify `WalletDataPanel.razor`, `LogPanel.razor`, `AliasesPanel.razor`, `LiveTrace.razor`, `SettingsPanel.razor`, `FaultPanel.razor`, `ResetPanel.razor`, `Placeholder.razor` (empty states: "No contacts yet.", "No bills due.", "No payments yet.", "No audit rows yet.", "No steps yet. Type in the phone.", "Arrives in milestone 10." kept; each an `EmptyState` component with an icon and `data-testid="empty-state"`), loading states (`MudSkeleton` rows the height of a table row while a grid loads the first time; `MudProgressLinear` kept for reloads), buttons (one filled brand button per panel at most; secondary actions outlined; Delete, Reset wallet and Clear all in danger; sizes consistent), `wwwroot/app.css` (`:focus-visible` with `--focus-ring` on buttons, inputs, selects, menu items and the phone's input; table header muted uppercase at `--text-xs`; the history Status column through `StatusBadge` with `done` success, `executing` warning, `failed` danger). Create `src/Agent.Web/Components/Console/EmptyState.razor`. Docs: `docs/agent-web.md` "Empty and loading states"; `docs/build-plan.md` milestone 12 gets a line pointing at this plan.

**Done when**

- `DesignTokensTests.EveryColourInTheStylesheetIsAToken` and `EveryTextPairingMeetsAA` still pass (the new pairings are in the table).
- `DesignTokensTests.FocusIsVisible`: `app.css` has a `:focus-visible` rule using `--focus-ring` and no rule sets `outline: none` outside it (the template's `h1:focus { outline: none }` goes).
- A new journey `EmptyStatesShowOnAFreshConsole`: on a console whose log is empty, `menu-log` then `empty-state` is visible; after one payment it is gone and a `log-row` shows.
- `AnUnknownProductIsRefusedAndLoggedWithTheProviderCode` and every other journey pass unchanged; all tests pass.

### Task 9: Documentation sweep and the checked screenshots

**Files.** `docs/agent-web.md` (every section touched above re-read as a whole; the layout Mermaid diagram shows the grid areas), `docs/architecture.md` section 6 (the `wwwroot/fonts` folder; the journey count), `docs/build-plan.md` milestone 12 ("The finished console in layout B": this plan, with the date), `docs/disclosure.md` (the font line from task 7 checked; nothing else was added), `docs/failure-modes.md` (no new failure mode: the pass changes no behaviour; one line in the commit message says so), `docs/reports/ui-pass-2026-10-<dd>/` (four PNG screenshots taken by a Playwright script in `tests/Agent.Web.Journeys` behind an environment variable `ZAINCASH_SCREENSHOTS=1`: the console at rest, the Hammoudi card live, the history grid on page 2, the settings panel; synthetic data only).

**Done when**

- Every "Done when" line of tasks 1 to 8 is listed in `docs/agent-web.md` with the test or file that shows it, as the build plan does for its milestones.
- The screenshots are committed and the README's console section links the one of the console at rest.
- `dotnet test` and `dotnet test tests/Agent.Web.Journeys` pass from a clean build; the counts are written in the commit message.

## Not planned, and why

- Dark mode, responsive layouts below 1,280 px beyond the safety net, animations: not in the brief.
- A cap on the live trace, "check again" in a new circuit, the reset of other tabs' phones: known gaps of `docs/agent-web.md`, each a behaviour change.
- The Learning data component: milestone 10; it will use `GridPager` and `EmptyState`.
- An accounts grid: needs a wallet route (decision 1).
- Any phone string beyond the two pager labels Khalil wrote on 21 Oct 2026 (D20).

## Review focus (checked by the implementer, beyond the tests above)

1. A grid whose list shrinks under the current page (an alias deleted on the last page): the page clamps and the pager updates (`PagedListTests.AShrunkListAfterADeleteKeepsAValidPage`).
2. A wallet change while the user is on page 3 of the history: the panel returns to page 1, so the newest row is visible; the contacts and aliases keep their page.
3. The settings panel's long Model table on a 900 px window: the middle column scrolls on its own, the phone stays in view.
4. The phone status bar's clock on a circuit that outlives midnight: the timer re-renders the time; the `TimeProvider` is the one the session uses, so the tests can freeze it.
5. A `closed` contact chosen as an option in `which_contact`: the question panel is unchanged by this pass; the refusal `account_closed` still comes from the checks, never from the badge.
