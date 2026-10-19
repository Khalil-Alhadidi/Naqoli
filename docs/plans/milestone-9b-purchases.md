# Milestone 9b: Store purchases (D11)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task (one implementer agent for the whole milestone, per Khalil's rule of 14 Oct 2026; the controller reviews each task's diff itself). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every deliverable of milestone 9b in `docs/build-plan.md`: the purchase flow against the catalogue under rules 11 and 12 as Khalil decided them, the purchase code shown on the phone with a copy button, and a My orders screen; the three deferred items closed (AMB-23 under rule 12, MAL-09 through configured operator spellings, NOGO-12's learning-log detail); rules 11 and 12 go to `yes` in `docs/rules-coverage.md`. Done when the purchase cases of the development set pass on the oracle, the purchase code never reaches the model (`OutboundPayloadTests` extended), and every log keeps only the last digits of a code. One screen journey per behaviour, on the oracle. GAP-06 is not built here (it waits for the milestone 10 prompt pass; the brief of 19 Oct 2026).

**Architecture:** The purchase path already exists end to end (`ProductResolver`, `Orchestrator.PurchaseAsync`, the card, the Confirm handler's last-four storage, `Purchase_code` on the phone). This milestone changes the resolver's two rules, reads operator spellings from the same configuration the bill resolver uses, formats one learning detail, adds a wallet read for orders through the conversation's turn, and gives the phone a copy button and an orders screen. No schema or prompt change: the model's structure already carries `operator_text`, `product_type` and `product_value`.

**Tech Stack:** .NET 10, Blazor Server with MudBlazor 9, EF Core, xUnit, Playwright for .NET. No new library: the copy button uses the browser's `navigator.clipboard` through a collocated JavaScript module, as `ReconnectModal.razor.js` is loaded today.

**Spec:** `docs/architecture.md` (D11), `docs/build-plan.md` (9b), `docs/matching-rules.md` (rules 11, 12, 17; "Added 16/10/2026", purchases), `docs/rules-coverage.md` (rows 11, 12, the 16/10 purchase row), the brief of 19 Oct 2026.

## Controller rulings (for Khalil to confirm)

1. **Operator spellings share `Agent:ProviderSpellings`.** The dictionary is keyed by biller code today (`internet`). It gains the provider codes `zain`, `asiacell` and `korek` as keys, so one setting holds every spelling of a provider or operator (the brief: "through the same configured spellings as billers"). Claude writes Latin spellings only (`asia` is the one MAL-09 needs); Arabic variants are Khalil's to add.
2. **The learning-log detail of `product_not_found`** is the provider code when the typed operator named one, else the typed operator text as the user wrote it, then the product type, then the value in dinars, space-separated, each part dropped when missing: NOGO-12 gives `korek credit 25000`.
3. **Product options name the type.** Rule 12's own example (a Zain card of ten: `ZN-CR-10` and `ZN-NET-10`) and the cross-operator list of AMB-23 need the credit or internet word on each option, or two options read the same. Two new phone strings, `Product_type_credit` and `Product_type_internet`. The card itself is unchanged (operator name, `Type_purchase`, the price); whether the card should also carry the type word is a suggestion for Khalil.
4. **The `product_not_found` reply stays one template.** NOGO-12's key lists `available` products; the runner marks that field `not graded` and the phone does not list them. Whether the reply should list what exists, as the case's note says, is Khalil's call (a template with a list built by code would do it).
5. **My orders** shows every order the wallet returns for the user, newest first (the wallet's order), each with its date in Iraq time, the product's operator name and type, the price, and the full code with a copy button. The list is read from the wallet under the conversation's turn like the alias directory; the trace records the count only. No cap: the seed holds few orders and the architecture names no limit.
6. **GAP-06** (`credit_to_own_number` with the operator, type and value filled) stays deferred to milestone 10, as the brief says; the tuning backlog's "Not on this list" paragraph already names it and is left as it is.
7. **Back from the orders screen reuses `Directory_Back`** (the same word); the header's second button is `Orders_Open`.

## Global constraints

- Rules 1 to 9 of `CLAUDE.md`. In particular: the purchase code is shown on the phone only; the audit row keeps `PurchaseCodeLast4` (`PendingStore.PurchaseCodeDigitsKept` = 4); the trace, the learning log and the application log never hold a code; the model never sees one.
- No Arabic in C#, Razor or test code. New phone strings are English placeholders starting `TODO-AR`, listed in `PhoneStringsTests.AwaitingKhalil` and in the controller's summary.
- Khalil's answer keys, `docs/matching-rules.md` and the sidecar `data/requests.dev.parses.jsonl` are not changed. A mismatch is reported, not patched.
- Settings are configuration shown in the settings component; `MaxProductsInOneQuestion` (8) is the rule 12 cap.
- The held-out set stays sealed (rule 7).
- One commit per task, Khalil's name only (no Co-Authored-By line).

## Review focus

Inputs the spec implies that no case exercises; each gets a test in the task that owns it:

1. A value and type with no operator where exactly one product fits (rule 11's example, internet 40): one option, never a card. (Task 1)
2. No operator, a type, no value, where the fitting products are 8 or fewer (an internet card: seven): one `which_product` list across the operators, in catalogue order within each price. (Task 1)
3. A Latin operator spelling that matches no configured spelling and no wallet name (`fastlink`): `product_not_found`, with the typed text in the learning detail. (Tasks 1, 2)
4. A wallet orders read that fails (the wallet down): the orders screen shows the unavailable line and the circuit stays up; nothing is traced beyond an error type. (Tasks 3, 4)
5. A purchase whose code has fewer than four characters (defensive: `PurchaseCodeLast4` takes the whole code) and a seeded order's code on the orders screen: both render, neither reaches the trace. (Task 3)

---

### Task 1: Rules 11 and 12, and operator spellings

**Files**
- Modify: `src/Agent.Core/Resolver/ProductResolver.cs`, `src/Agent.Core/AgentOptions.cs` (the `ProviderSpellings` summary: by biller code or provider code), `src/Agent.Web/appsettings.json` and `src/Agent.Eval/appsettings.json` (`ProviderSpellings` gains `"zain": ["zain"], "asiacell": ["asia", "asiacell", "asia cell"], "korek": ["korek"]`), `docs/agent-core.md` ("Bill and product": the two rules as built, a Mermaid flowchart of the resolver; "Settings": the `ProviderSpellings` row)
- Test: `tests/Agent.Core.Tests/Resolver/ProductResolverTests.cs`, `tests/Agent.Core.Tests/Pipeline/OrchestratorTests.cs`

**Interfaces**
- Produces: `ProductResolver.Resolve(ParsedRequest request, IReadOnlyList<WalletProduct> catalogue, AgentOptions options)` keeps its signature. New behaviour with no operator named: the fitting products (type and value filters as today) number `options.MaxProductsInOneQuestion` or fewer → `ProductResolution.Options` (ordered by price, catalogue order within a price), one product included; more → `ProductResolution.AskOperator` with the providers of the fitting products in catalogue order; none → `NotFound`. With an operator named: unchanged (one fit → `Card`, several → `Options`, none → `NotFound`).
- Produces: `ProductResolution.NotFound(string? ProviderCode)`: the provider the typed operator named, or null when none was named or none matched. (Task 2 reads it.)
- Produces: `ProvidersNamed` also matches `options.ProviderSpellings[providerCode]` for each provider in the catalogue, compared as today (`ArabicText.Key` of the lower-cased text equals the key of the spelling).

- [ ] **Step 1: `ProductResolverTests`.** Change `InternetFortyWithNoOperatorAsksTheOperatorEvenWithOneFit` to `InternetFortyWithNoOperatorListsTheOneFittingProductAsAnOption` (`Options` with `["ZN-NET-40"]`); change `CreditTenWithNoOperatorAsksAmongThree` to `CreditTenWithNoOperatorListsThreeProductsAcrossOperators` (`Options` with `["ZN-CR-10", "AC-CR-10", "KR-CR-10"]`); add `CreditFiveWithNoOperatorListsThreeProducts` (AMB-23: `["ZN-CR-5", "AC-CR-5", "KR-CR-5"]`), `InternetWithNoOperatorAndNoValueListsSevenProducts` (the seven internet cards, ordered by price then catalogue), `NothingNamedAsksAmongEveryOperator` stays (15 fit, above 8), `WithACapOfTwoCreditTenAsksTheOperator` (`MaxProductsInOneQuestion = 2` → `AskOperator` with the three providers), `AConfiguredLatinSpellingNamesTheOperator` (`new AgentOptions { ProviderSpellings = { ["asiacell"] = ["asia"] } }`, `Resolve("asia", "credit", 5_000)` → `Card` `AC-CR-5`), `AConfiguredSpellingWithASpaceMatches` (`"asia cell"` configured, typed `"asia cell"` and `"asiacell"` both → the card), `AnUnconfiguredLatinSpellingIsNotFoundWithNoProvider` (`fastlink` → `NotFound` with `ProviderCode` null), `NotFoundCarriesTheProviderCode` (`Resolve("korek", "credit", 25_000)` → `NotFound` with `"korek"`). Run: fail.
- [ ] **Step 2: Implement** the resolver, the options summary, the two settings files. Run the class: pass.
- [ ] **Step 3: `OrchestratorTests`.** Change `PurchaseWithoutOperatorAsks` to assert `which_product` with the three AMB-23 option values when the request is `ParsedRequest.Purchase(null, "credit", 5_000)`; add `PurchaseWithNothingNamedAsksTheOperator` (AMB-24's parse → `which_operator` with `zain`, `asiacell`, `korek`) if no such test exists. Run: pass.
- [ ] **Step 4: Docs** (`docs/agent-core.md`). Run the whole core suite: green, 0 warnings.
- [ ] **Step 5: Commit**: `feat(core): rules 11 and 12 as decided (a bare value lists the fitting products across operators, up to MaxProductsInOneQuestion; above it the operator is asked first); operator spellings read from Agent:ProviderSpellings by provider code (MAL-09)`.

### Task 2: The learning-log detail and the product type on options

**Files**
- Modify: `src/Agent.Core/Pipeline/AgentReply.cs` (`Option` gains `string? ProductType = null` as a trailing optional parameter), `src/Agent.Core/Pipeline/Orchestrator.cs` (`ProductOptions` sets `ProductType = p.Type`; the `NotFound` branches of `PurchaseAsync` pass `LearningLogRule.ProductDetail(...)` as the refusal's detail), `src/Agent.Core/Learning/LearningLogRule.cs`
- Test: `tests/Agent.Core.Tests/Learning/LearningLogRuleTests.cs`, `tests/Agent.Core.Tests/Pipeline/OrchestratorTests.cs`, `tests/Agent.Core.Tests/Conversation/ConversationTests.cs`

**Interfaces**
- Produces: `public static string? LearningLogRule.ProductDetail(string? providerCode, string? operatorText, string? productType, long? productValue)`: ruling 2; null when every part is missing. `DetailFrom(ProductNotFound, request)` calls it with `providerCode` null, so the fallback keeps today's shape for any caller that passes no detail.
- Produces: `Option.ProductType` (`credit` or `internet`) on every product option; null on every other option. Task 4 renders it.
- Consumes: `ProductResolution.NotFound.ProviderCode` (Task 1).

- [ ] **Step 1: `LearningLogRuleTests`**: `ProductDetailIsTheProviderCodeTypeAndValue` (`("korek", "typed", "credit", 25_000)` → `"korek credit 25000"`), `ProductDetailFallsBackToTheTypedOperator` (`(null, "fastlink", "credit", 5_000)` → `"fastlink credit 5000"`), `ProductDetailDropsMissingParts` (`(null, null, "internet", null)` → `"internet"`; all null → null), `AProductNotFoundRefusalWithADetailKeepsIt`. Run: fail. Implement. Run: pass.
- [ ] **Step 2: `OrchestratorTests`**: `AnUnknownProductIsRefusedWithTheProviderCodeAsDetail` (NOGO-12's parse `Purchase("korek", "credit", 25_000)` through the orchestrator against the host's wallet → `Refusal` `product_not_found` with `Detail` `"korek credit 25000"`), `ProductOptionsCarryTheirType` (AMB-23's parse → every option's `ProductType` is `credit`). Run: fail. Implement. Run: pass.
- [ ] **Step 3: `ConversationTests`** on the oracle over the dev set (as `ConversationAnswerTests` plays cases by id through `DevSetText`): `ABareValueListsTheMatchingProductsAndTheTapGivesTheCard` (AMB-23: `which_product` with the three values; tap `KR-CR-5`; a card whose `ProductCode` is `KR-CR-5` and amount 5,000), `ALatinOperatorSpellingGoesStraightToTheCard` (MAL-09 on U2 with the harness configured `o.ProviderSpellings["asiacell"] = ["asia"]`; a card `AC-CR-5`), `AnUnknownProductWritesTheLearningRowWithTheProviderCode` (NOGO-12: no card, the learning row `product_not_found` with detail `korek credit 25000`). Run: pass (if one fails, the defect is in Tasks 1 or 2, not the key).
- [ ] **Step 4: Oracle run** with the wallet running: `dotnet run --project src/Agent.Eval -- dev --label m9b`. Expected: 89 of 95; the six misses are CMP-01, CMP-03, CMP-05, CMP-07, CMP-09 (milestone 9c) and GAP-06 (milestone 10); AMB-23, MAL-09 and NOGO-12 pass; every purchase case passes; 0 executions without a tap, 0 masking failures. Commit the two report files with the code.
- [ ] **Step 5: Commit**: `feat(core): product_not_found is logged as provider code, type and value (NOGO-12); product options carry their type; oracle m9b report (89 of 95, the 9c and GAP-06 items only)`.

### Task 3: Orders through the wallet and the conversation; the code never reaches the model

**Files**
- Create: `src/Agent.Core/Store/PurchaseOrder.cs`, `src/Agent.Core/Store/OrdersReader.cs`
- Modify: `src/Agent.Core/Wallet/WalletContracts.cs` (`WalletOrder`), `src/Agent.Core/Wallet/WalletClient.cs` (`GetOrdersAsync`), `src/Agent.Core/Conversation/Conversation.cs` (`ListOrdersAsync`), `src/Agent.Core/Trace/TraceSteps.cs` (`OrdersRead = "orders_read"`), `src/Agent.Core/DependencyInjection.cs` (`AddScoped<OrdersReader>()`), `docs/agent-core.md` (a section "Store purchases (milestone 9b)": the resolver's rules, the code's path from the wallet's response to the phone and the last four digits, My orders; a Mermaid sequence diagram of Confirm → wallet → `Done(PurchaseCode)` → phone, with what each log keeps)
- Test: `tests/Agent.Core.Tests/Agent/WalletClientTests.cs`, `tests/Agent.Core.Tests/Conversation/ConversationTests.cs`, `tests/Agent.Core.Tests/Parsing/OutboundPayloadTests.cs`, `tests/Agent.Core.Tests/Trace/TraceSinkTests.cs`

**Interfaces**
- Produces: `public sealed record WalletOrder(int OrderId, int TransactionId, int ProductId, string ProductCode, string Code, DateTimeOffset CreatedAt)` with `ToString()` overridden to print `Code` as `***` (as `WalletPaymentRequest` prints the PIN), so no log or trace formatting leaks it.
- Produces: `WalletClient.GetOrdersAsync(int userId, CancellationToken cancellationToken = default)` → `Task<WalletResult<IReadOnlyList<WalletOrder>>>` over `GET users/{id}/orders` (route name `orders`), through `GetAsync<T>` like the other reads.
- Produces: `public sealed record PurchaseOrder(int OrderId, string Code, DateTimeOffset CreatedAt, string? ProviderNameAr, string? ProductType, long? PriceIqd)` (`ToString()` masks `Code` the same way); the product fields null when the catalogue no longer has the product.
- Produces: `OrdersReader` (scoped; `WalletClient`, `ITraceSink`) with `Task<WalletResult<IReadOnlyList<PurchaseOrder>>> ListAsync(int userId, CancellationToken)`: reads the catalogue and the orders, joins by `ProductId`, keeps the wallet's order, traces `orders_read` with the count as detail, and returns the first failing wallet result unchanged.
- Produces: `Conversation.ListOrdersAsync()` → `Task<WalletResult<IReadOnlyList<PurchaseOrder>>>`, under the turn and in its own scope exactly as `ListAliasesAsync` is (the same `Unavailable` answers for a stopped conversation and for an exception). It adds no message and does not change `WalletVersion`.

- [ ] **Step 1: `WalletClientTests`**: `OrdersAreReadNewestFirstAndCarryTheCode` (U1 through the host's wallet: the seed's orders for U1, as the wallet lists them; each `Code` non-empty; `ToString()` of an order does not contain its code). Run: fail. Implement `WalletOrder`, `GetOrdersAsync`. Run: pass.
- [ ] **Step 2: `ConversationTests`**: `ListingOrdersJoinsTheCatalogueAndTracesOnlyTheCount` (U3 as in `TheTraceKeepsNoPinKeyOrCodeAfterAPinPaymentAndAPurchase`: a purchase confirmed, then `ListOrdersAsync` → `Ok`; the first order's `Code` equals `bought.PurchaseCode`, its `ProviderNameAr`, `ProductType` and `PriceIqd` are the product's; the trace has one `orders_read` entry with the count; no entry's step or detail contains any order's code; the conversation's message count is unchanged), `ListingOrdersWhileStoppedIsUnavailable`. Extend `TheTraceKeepsNoPinKeyOrCodeAfterAPinPaymentAndAPurchase`: after the purchase, read the audit rows of the purchase record and assert the Done row's detail or reference holds the code's last four characters and not the whole code (through `RecordAsync`: `PurchaseCodeLast4` equals `bought.PurchaseCode[^4..]`). Run: fail. Implement `PurchaseOrder`, `OrdersReader`, `ListOrdersAsync`, the step, the registration. Run: pass.
- [ ] **Step 3: `TraceSinkTests.NoPinKeyCodeOrPhoneInAnyStep`**: include `TraceSteps.OrdersRead` in whatever list that test walks. Run: pass.
- [ ] **Step 4: `OutboundPayloadTests`**: add `APurchaseCodeNeverReachesTheModel` beside the two existing tests, sharing `PlayAndCheckAsync` through a third flag `confirmPurchase`: after the purchase card, `conversation.ConfirmAsync(null)` → `Done` with a non-null `PurchaseCode`; then `conversation.ListOrdersAsync()` → `Ok` whose first order's code is that code; then the balance question and, in the alias variant, the alias sentence as today. Assert: the purchase code, every order code returned, and every seeded purchase code of `SeedFacts.Seed` (the history rows of type `purchase`, whatever the seed set calls them) are absent from every parser message and from the schema, both as typed and in Arabic-Indic digits (`ArabicDigits.ToArabicIndic`); the `Pending:` note after the purchase names no code. The existing assertions still hold. Run: pass (no code change expected; a failure means the conversation leaks, not the test).
- [ ] **Step 5: Docs** (`docs/agent-core.md`). Whole core suite: green, 0 warnings.
- [ ] **Step 6: Commit**: `feat(core): My orders read through the wallet and the conversation's turn (OrdersReader, Conversation.ListOrdersAsync; the trace keeps the count only); the purchase code never reaches the model (OutboundPayloadTests) and the audit keeps its last four digits`.

### Task 4: The phone: the copy button, the product type on options, My orders

**Files**
- Create: `src/Agent.Web/Components/Phone/CopyButton.razor`, `src/Agent.Web/Components/Phone/CopyButton.razor.js`, `src/Agent.Web/Components/Phone/OrdersPanel.razor`
- Modify: `src/Agent.Web/Services/ChatSession.cs` (`ListOrdersAsync()` → `Task<WalletResult<IReadOnlyList<PurchaseOrder>>>` as `ListAliasesAsync` is wrapped, with the same restart re-read; `LocaliseOutcome`: the `Purchase_code` message carries `ChatAction.CopyCode`; `OptionText` and the question's option label include the type word), `src/Agent.Web/Services/ChatMessage.cs` (`ChatAction.CopyCode`; `ChatMessage.CopyText` string?, the full code, in-memory only), `src/Agent.Web/Components/Phone/ChatPanel.razor` (a `CopyButton` under the `Purchase_code` message), `src/Agent.Web/Components/Phone/QuestionPanel.razor` (`Label`: `display + " " + Text["Product_type_" + type] + " " + amount` when `option.ProductType` is set), `src/Agent.Web/Components/Phone/PhoneFrame.razor` (a `screen` enum `Chat`, `Directory`, `Orders`; header buttons `phone-directory`, `phone-orders`, back buttons `phone-directory-back`, `phone-orders-back`; the compose row only on `Chat`), `src/Agent.Web/wwwroot/app.css` (the orders rows, the copy button), `src/Agent.Web/Services/PhoneFormat.cs` (`Date(DateTimeOffset)`: the Iraq date `yyyy/MM/dd` in Arabic-Indic digits, via `AgentClock.IraqOffset`), `src/Agent.Web/Resources/PhoneStrings.resx` (the keys below, `TODO-AR`), `tests/Agent.Core.Tests/Web/PhoneStringsTests.cs` (`AwaitingKhalil` lists them), `docs/agent-web.md` ("The session flow": the copy action and the orders screen; "Option taps": the type word; "Figures in the phone": `Date`; a section "Store purchases (milestone 9b)" with a Mermaid flow chat → orders → copy; "The phone strings, for Khalil": the new keys; "The `data-testid` contract": the new ids)
- Test: `tests/Agent.Core.Tests/Web/ChatSessionTests.cs`, `tests/Agent.Core.Tests/Web/PhoneFormatTests.cs` (if present; else add the date case to the nearest formatting test class)

**Interfaces**
- Consumes: `Conversation.ListOrdersAsync`, `PurchaseOrder` (Task 3); `Option.ProductType` (Task 2).
- `CopyButton` parameters: `string Text` (what to copy), `string TestId`. On click it calls the module's `copyText(text)` (`navigator.clipboard.writeText`); on success it sets `data-copied="true"` and shows `Purchase_code_copied` for 2 seconds, then `Purchase_code_copy` again; on failure it leaves `data-copied="false"` and the label unchanged. The module is loaded with `IJSRuntime.InvokeAsync<IJSObjectReference>("import", "./Components/Phone/CopyButton.razor.js")` once per instance and disposed with it. The copied text is never logged or traced.
- `OrdersPanel` (`data-testid="phone-orders-screen"`): title `Orders_Title`; `Orders_Empty` when none; one row per order (`data-testid="order-row"`, `data-order-id`): the product line (`order-product`: operator name, the type word, the price through `PhoneFormat.Amount`), the date (`order-date`, `PhoneFormat.Date`), the code (`order-code`, `PhoneFormat.Digits(code)`) and a `CopyButton` (`TestId` `order-copy`). The unavailable line is `Refusal_not_done_nothing_taken`, as the directory shows it. Reloads on `Session.WalletVersion` like `DirectoryPanel`.
- Resource keys, all `TODO-AR` English placeholders: `Orders_Open` (header button), `Orders_Title`, `Orders_Empty`, `Purchase_code_copy` (button), `Purchase_code_copied` (button, after a copy), `Product_type_credit`, `Product_type_internet`.
- `data-testid`s: `phone-orders`, `phone-orders-back`, `phone-orders-screen`, `order-row`, `order-product`, `order-date`, `order-code`, `order-copy`, `purchase-code-copy` (the chat's button).

- [ ] **Step 1: `ChatSessionTests`**: `TheDonePurchaseOutcomeCarriesTheCodeMessageWithTheCopyAction` (a confirmed purchase: the second Done message is `Purchase_code` with the code in Arabic-Indic digits, `Action` `CopyCode`, `CopyText` the code as the wallet gave it), `ProductOptionsAreLabelledWithTheTypeWord` (AMB-23's question: each option's text through `OptionText`, or the label helper the panel uses, contains `Product_type_credit`), `ListOrdersReturnsTheConversationsOrders`. `PhoneFormat.Date`: `ADateIsShownAsTheIraqDateInArabicIndicDigits` (an instant at 23:30 UTC prints the next Iraq day). Run: fail. Implement the session members, the action, the format, the keys, the allow-list. Run: pass.
- [ ] **Step 2: Build the components**, the header, the stylesheet. `dotnet build`: 0 warnings. Run the console by hand once (wallet first): a purchase on CLN-13's sentence shows the code and the copy button; the orders screen lists it first with the seed's orders after; the directory still opens and closes.
- [ ] **Step 3: Docs** (`docs/agent-web.md`). Commit: `feat(web): the purchase code with a copy button, product options labelled with their type, and the phone's My orders screen (milestone 9b; strings TODO-AR)`.

### Task 5: The journeys

**Files**
- Create: `tests/Agent.Web.Journeys/PurchaseJourneys.cs`
- Modify: `tests/Agent.Web.Journeys/JourneyBase.cs` (`InitializeAsync`: the context is created with `Permissions = ["clipboard-read", "clipboard-write"]`; a helper `ClipboardAsync(IPage)` → `page.EvaluateAsync<string>("() => navigator.clipboard.readText()")`), `tests/Agent.Web.Journeys/JourneyHost.cs` (`OrdersAsync(int userId)` → `IReadOnlyList<WalletOrder>` through the host's wallet client), `docs/agent-web.md` ("The journeys" table)

**Interfaces**
- Consumes: the `data-testid`s of Task 4; `DevSet.Say`; `JourneyHost.BalanceAsync`, `AgentRowsAsync`, `LearningRowsAsync`; the trace entries through the live trace's `trace-entry` elements.

One journey per behaviour, on the oracle; every code, count and name read from the wallet or the dev set, never typed:
- [ ] `ANamedProductGoesStraightToTheCardAndTheCodeShowsWithACopyButton`: U1, CLN-13; a card with `card-display` the product's operator name and `card-amount` 25,000; confirm; `Outcomes(page, "done")` has two entries, the second containing the digits of the new order's code (`Host.OrdersAsync(U1)` first row, which is new against the count before); `purchase-code-copy` is visible; click it; `ClipboardAsync` equals the code and the button has `data-copied="true"`.
- [ ] `TheCodeIsKeptOnlyByItsLastFourDigitsInTheLogs`: after the same purchase, `AgentRowsAsync` has a row containing the code's last four characters and no row containing the whole code, in either digit form; no `trace-entry` text contains the whole code.
- [ ] `ABareValueListsTheMatchingProductsAcrossOperators`: U1, AMB-23; `which_product` with option values `ZN-CR-5`, `AC-CR-5`, `KR-CR-5` (set equality), each option's text containing `Product_type_credit`; tap `KR-CR-5`; a card for 5,000; confirm; balance 745,000.
- [ ] `NothingNamedAsksTheOperatorFirstThenTheProducts`: U1, AMB-24; `which_operator` with `zain`, `asiacell`, `korek`; tap `korek`; `which_product` with the four Korek codes; tap `KR-NET-30`; a card for 30,000.
- [ ] `ALatinOperatorSpellingGoesStraightToTheCard`: U2 (`Host.UserWebAddress(U2)`), MAL-09; a card for 5,000 whose `card-display` is Asiacell's Arabic name from the catalogue; confirm; balance 13,500.
- [ ] `AnUnknownProductIsRefusedAndLoggedWithTheProviderCode`: U1, NOGO-12; no card; the last agent message is `Refusal_product_not_found`; `LearningRowsAsync` has `("product_not_found", "korek credit 25000")`; balance unchanged.
- [ ] `MyOrdersListsThePurchaseCodesNewestFirstAndCopies`: U1, CLN-13 confirmed; `phone-orders`; `order-row` count equals `Host.OrdersAsync(U1).Count`; the first row's `order-code` digits equal the newest order's code and its `order-product` contains the operator name and `Product_type_internet`; click its `order-copy`; the clipboard equals the code; `phone-orders-back` returns to the chat with the input visible.
- [ ] `ACancelledPurchaseIssuesNoCode`: U3, CLN-29; a card; `card-cancel`; `Outcomes(page, "cancelled")` one; `Host.OrdersAsync(U3).Count` unchanged; balance 12,500,000.
- [ ] Run `dotnet test tests/Agent.Web.Journeys`: green (58 journeys). Commit: `test(journeys): eight purchase journeys on the oracle (milestone 9b)`.

### Task 6: Coverage, failure modes, status

**Files**
- Modify: `docs/rules-coverage.md` (rows 11 and 12 → `yes` with `ProductResolver.Resolve`, the Task 1 tests and the journeys; the 16/10 purchase row → `yes`; the header's survey note gains 19 Oct 2026; the totals; "Not yet built" loses 9b), `docs/failure-modes.md` (three rows: a user who misses the code finds it under My orders; a code in a log is only its last four digits (`PendingStore.PurchaseCodeDigitsKept`, `WalletOrder.ToString`, `OrdersReader` traces the count); an operator spelled in a way no configuration knows is `product_not_found` and logged with the typed text, for Khalil to add the spelling), `docs/build-plan.md` (the 9b status: done, date, the commits, the controller rulings, the oracle and model figures, GAP-06 deferred), `docs/agent-core.md` ("Known limits": GAP-06)
- [ ] Write the rows; run the whole suite once more (core and journeys); commit: `docs: rules 11 and 12 are built (rules-coverage); purchase failure modes; the 9b status`.

## After the tasks (controller)

- Sonnet rerun with the wallet running: `dotnet run --project src/Agent.Eval -- dev --parser claude:sonnet --repeat 3 --label m9b`; commit the reports; record the scores in the build plan's 9b status.
- `dotnet-security-reviewer` agent over the milestone's diff; fixes through the implementer; the review saved as `docs/reviews/milestone-9b-security-review.md`.
- The list of `TODO-AR` keys for Khalil in the summary.
- Push; tag `milestone-9b`.
