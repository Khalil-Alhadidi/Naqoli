# Model Providers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A parse-call candidate from any OpenAI-compatible vendor (Gemini, OpenRouter) is one entry in the `Model` section, switched by `Model:Candidate`, measured by the same runner and reported next to the Anthropic candidates.

**Architecture:** `ModelCandidate` gains a second provider, `compatible`, with its address, key setting, output mode and pacing. `ParserFactory` builds an `IChatClient` from the OpenAI SDK for that provider and reads the candidate's own key. `ClaudeParser` shapes the request by provider (no cache control, no effort, optional schema block for `json_object`), paces calls through one shared `ParsePacer`, and reads usage from `UsageDetails`. The runner accepts `compatible:<candidate>`, copies only `<Vendor>:ApiKey` secrets, and records provider, mode and pacing in the report header.

**Tech Stack:** .NET 10, Microsoft.Extensions.AI 10.11.0, Anthropic 12.56.0 (unchanged), new `OpenAI` SDK and `Microsoft.Extensions.AI.OpenAI` (latest stable whose Abstractions dependency matches 10.11.0; check with `dotnet package search Microsoft.Extensions.AI.OpenAI --exact-match`), xUnit v3.

**Spec:** `docs/history/plans/model-providers.md`

## Global Constraints

- Rule 2 (one read-only parse call per message, strict structure validated by code), rule 5 (masking before the text leaves), rule 9 (no secrets in the repository): unchanged.
- `src/Agent.Core/Parsing/parse-prompt.md` is not edited (version 6 stays).
- No Arabic in C#; no new phone string.
- Every new package goes in `docs/disclosure.md` in the commit that adds it.
- No retries on any model client. A 429 is one failed call.
- Settings are configuration and appear read-only on the settings panel.
- Never open `data/requests.heldout.jsonl` or `zaincash_testset_v0.10.xlsx`.
- Commits carry Khalil's name only, no Co-Authored-By line (memory rule, 15 Oct 2026). Commit messages follow the repository style: `type(scope): what, in one line, with the test counts`.
- Run `dotnet build` and the named test classes before each commit; `dotnet test tests/Agent.Core.Tests` at the end of Tasks 3, 4 and 5.

## Review Focus

1. **A vendor that answers `max_completion_tokens` with HTTP 400.** The reply must be the handover (`ParserUnavailableException`, reason `http_400`), never a crash, and the log must not carry the response body. Pinned in Task 3 (`CompatibleAdapterTests.AnHttpFailureIsUnavailableWithoutTheKeyOrTheBody`).
2. **A `json_object` reply that is valid JSON but not the structure** (a bare object with a `requests` key missing, or an extra field). Must be the `json` problem, no card. Pinned in Task 3 (`CompatibleAdapterTests.AnOffSchemaJsonObjectReplyIsTheJsonProblem`).
3. **Two scoped parsers in two sessions on the same paced candidate.** The gap must hold across scopes, so the pacer is a singleton keyed by candidate name. Pinned in Task 3 (`ClaudeParserTests.TwoParsersShareOnePacePerCandidate`).
4. **A candidate whose `ApiKeySetting` names a non-key setting** (`ConnectionStrings:Agent`). Must fail the start with a message naming the setting, and the runner must copy nothing. Pinned in Task 1 (validation) and Task 4 (`HeldOutGuardTests.TheRunnerCopiesOnlyAVendorKeyShapedSetting`).
5. **A `compatible` candidate selected with `Agent:Parser` still `claude`.** Must work: `claude` is a synonym of `model`. Pinned in Task 2 (`ParserFactoryTests.ClaudeIsASynonymOfModelForACompatibleCandidate`).

---

### Task 1: The `compatible` provider in `ModelOptions`

**Files:**
- Modify: `src/Agent.Core/Parsing/ModelOptions.cs`
- Create: `tests/Agent.Core.Tests/Parsing/ModelOptionsTests.cs`

**Interfaces:**
- Produces, on `ModelOptions`: `public const string CompatibleProvider = "compatible";`, `public const string OutputModeJsonSchema = "json_schema";`, `public const string OutputModeJsonObject = "json_object";`, `public static IReadOnlyList<string> Providers { get; }` (`anthropic`, `compatible`), `public static IReadOnlyList<string> OutputModes { get; }`, `public static string LabelOf(string provider)` (`anthropic` gives `claude`, `compatible` gives `compatible`, anything else throws `InvalidOperationException`).
- Produces, on `ModelCandidate`: `public string? BaseUrl { get; set; }`, `public string? ApiKeySetting { get; set; }`, `public string OutputMode { get; set; } = ModelOptions.OutputModeJsonSchema;`, `public int MinMillisecondsBetweenCalls { get; set; }` (default 0), `public bool IsAnthropic => Provider == ModelOptions.AnthropicProvider;`, `public bool IsCompatible => Provider == ModelOptions.CompatibleProvider;`, `public string Label => ModelOptions.LabelOf(Provider);`.
- Produces: `public static bool IsVendorKeySetting(string? setting)` on `ModelOptions`, true for `^[A-Za-z][A-Za-z0-9]*:ApiKey$`.
- `Problems()` keeps every existing line for `anthropic` and adds, for every provider, `MinMillisecondsBetweenCalls must not be below 0.`; for `compatible`: `BaseUrl must be an absolute https address.`, `ApiKeySetting must be '<Vendor>:ApiKey'.`, `OutputMode must be one of json_schema, json_object.`; the `Effort` and `PromptCacheTtl` lines apply only to `anthropic`; the `Provider` line becomes `Provider must be one of anthropic, compatible.` Every message keeps the `Model:Candidates:<name>:` prefix.

- [ ] **Step 1: Write the failing tests** in `ModelOptionsTests` (builder: the `Options()` helper of `ClaudeParserTests` is `internal static`; add a sibling `Compatible()` helper in the new class returning a valid `compatible` candidate named `gemini` with `BaseUrl = "https://generativelanguage.googleapis.com/v1beta/openai"`, `ApiKeySetting = "Gemini:ApiKey"`, `ModelId = "gemini-test"`, prices 0, `CacheWritePricePerMTok = 0`):

```csharp
[Fact] public void AValidCompatibleCandidateHasNoProblems()            // Problems() is empty
[Fact] public void AnUnknownProviderIsOneProblem()                      // "Provider must be one of anthropic, compatible."
[Theory] [InlineData(null)] [InlineData("http://x")] [InlineData("relative/path")]
public void ACompatibleCandidateNeedsAnAbsoluteHttpsBaseUrl(string? url)
[Theory] [InlineData(null)] [InlineData("ConnectionStrings:Agent")] [InlineData("Gemini:Secret")] [InlineData("1Gemini:ApiKey")]
public void ACompatibleCandidateNeedsAVendorKeySetting(string? setting) // message ends "ApiKeySetting must be '<Vendor>:ApiKey'."
[Fact] public void AnUnknownOutputModeIsOneProblem()
[Fact] public void NegativePacingIsOneProblemOnEitherProvider()
[Fact] public void EffortAndCacheTtlAreNotCheckedOnACompatibleCandidate() // Effort = "silly", PromptCacheTtl = "2h": no problem
[Theory] [InlineData("anthropic", "claude")] [InlineData("compatible", "compatible")]
public void TheLabelFollowsTheProvider(string provider, string label)
[Theory] [InlineData("Gemini:ApiKey", true)] [InlineData("OpenRouter:ApiKey", true)] [InlineData("Anthropic:ApiKey", true)] [InlineData("ConnectionStrings:Agent", false)] [InlineData("Gemini:ApiKey:Extra", false)]
public void AVendorKeySettingIsVendorColonApiKey(string setting, bool expected)
```

- [ ] **Step 2: Run** `dotnet test tests/Agent.Core.Tests -- --filter-class "Agent.Core.Tests.Parsing.ModelOptionsTests"`. Expected: build failure (members missing).

- [ ] **Step 3: Implement** the Interfaces block in `ModelOptions.cs`. `IsVendorKeySetting` uses a `[GeneratedRegex]`. `BaseUrl` check: `Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps`.

- [ ] **Step 4: Run** the class again, then `dotnet test tests/Agent.Core.Tests -- --filter-class "Agent.Core.Tests.Parsing.ClaudeParserTests"`. Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Agent.Core/Parsing/ModelOptions.cs tests/Agent.Core.Tests/Parsing/ModelOptionsTests.cs
git commit -m "feat(core): the compatible provider on ModelCandidate: BaseUrl, ApiKeySetting (<Vendor>:ApiKey), OutputMode, MinMillisecondsBetweenCalls, and the per-provider validation (docs/history/plans/model-providers.md); ModelOptionsTests N passed"
```

---

### Task 2: The OpenAI-compatible client in `ParserFactory`, and `Agent:Parser` `model`

**Files:**
- Modify: `src/Agent.Core/Agent.Core.csproj` (add `OpenAI` and `Microsoft.Extensions.AI.OpenAI`)
- Modify: `src/Agent.Core/Parsing/ParserFactory.cs`
- Modify: `src/Agent.Core/AgentOptions.cs:194` (regex `^(scripted|claude|model)$`)
- Modify: `docs/disclosure.md` (two library lines under the libraries list, after the Microsoft.Extensions.AI line)
- Test: `tests/Agent.Core.Tests/Parsing/ParserFactoryTests.cs`

**Interfaces:**
- Consumes: Task 1's `ModelCandidate.IsCompatible`, `BaseUrl`, `ApiKeySetting`.
- Produces: `public const string Model = "model";` on `ParserFactory`; `AddAgentParser` treats `model` and `claude` alike.
- Produces: `public static string? ApiKey(IConfiguration configuration, ModelCandidate candidate, Func<string, string?>? environment = null)`: for `anthropic` the existing two-place lookup; for `compatible` `configuration[candidate.ApiKeySetting]` (null when blank). The existing `ApiKey(IConfiguration, Func<string,string?>?)` stays for the Anthropic callers.
- Produces: `public static string MissingKeyMessageFor(ModelCandidate candidate)`: the existing `MissingKeyMessage` for `anthropic`; for `compatible`: `"Agent:Parser is 'model' but no key is set for the candidate. Set " + candidate.ApiKeySetting + " in user secrets (dotnet user-secrets set \"" + candidate.ApiKeySetting + "\" <key> --project src/Agent.Web) or the environment variable " + candidate.ApiKeySetting.Replace(':', '_').Replace("_", "__") + "."` (builder: the env form is the setting with `:` replaced by `__`).
- Produces: `public static IChatClient CreateChatClient(string apiKey, ModelCandidate candidate)` now branches: `anthropic` unchanged; `compatible` returns `CreateCompatibleClient(apiKey, candidate)`.
- Produces: `public static IChatClient CreateCompatibleClient(string apiKey, ModelCandidate candidate, HttpClient? httpClient = null)`: `new OpenAIClient(new ApiKeyCredential(apiKey), new OpenAIClientOptions { Endpoint = new Uri(candidate.BaseUrl!), RetryPolicy = new ClientRetryPolicy(maxRetries: 0), Transport = httpClient is null ? null : new HttpClientPipelineTransport(httpClient) })`, then `.GetChatClient(candidate.ModelId).AsIChatClient()`.
- `ModelOptionsValidator` takes `Func<ModelCandidate, bool> hasKey` and adds `MissingKeyMessageFor(candidate)` when false.

- [ ] **Step 1: Add the packages** to `Agent.Core.csproj` and run `dotnet build`. Expected: success. Add the two disclosure lines.

- [ ] **Step 2: Write the failing tests** in `ParserFactoryTests` (reuse its `Build(settings, environmentKey)` helper; add a `compatibleSettings` dictionary helper with `Model:Candidate=gemini`, the `gemini` candidate's five settings, and `Agent:Parser=model`):

```csharp
[Fact] public void ModelWithACompatibleCandidateAndItsKeyGivesTheClaudeParser()   // settings["Gemini:ApiKey"] = SecretLookingKey; IParser is ClaudeParser; Name == "compatible:gemini"
[Fact] public void ClaudeIsASynonymOfModelForACompatibleCandidate()              // Agent:Parser=claude, same settings: starts
[Fact] public void ACompatibleCandidateWithoutItsKeyFailsNamingTheSetting()      // message contains "Gemini:ApiKey" and "Gemini__ApiKey"; not the Anthropic names
[Fact] public void ACompatibleCandidateIgnoresTheAnthropicKey()                  // Anthropic:ApiKey set, Gemini:ApiKey not: fails
[Fact] public void TheCompatibleClientNeverRetriesAndTargetsTheBaseUrl()         // CreateCompatibleClient with a stub HttpClient; one request; request URI starts with the BaseUrl; exactly one HTTP call on a 500
[Fact] public void AnUnknownParserIsRefused()                                     // existing; message now names scripted, claude and model
```

- [ ] **Step 3: Run** `dotnet test tests/Agent.Core.Tests -- --filter-class "Agent.Core.Tests.Parsing.ParserFactoryTests"`. Expected: build failure.

- [ ] **Step 4: Implement** the Interfaces block. In `AddAgentParser`, `case Claude: case Model:` share one body; the validator's `hasKey` is `c => ApiKey(configuration, c, environment) is not null`; the singleton `IChatClient` factory calls `ApiKey(configuration, candidate, environment)`.

- [ ] **Step 5: Run** the class, then `ClaudeParserTests` and `AnthropicAdapterTests`. Expected: all PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Agent.Core/Agent.Core.csproj src/Agent.Core/Parsing/ParserFactory.cs src/Agent.Core/AgentOptions.cs docs/disclosure.md tests/Agent.Core.Tests/Parsing/ParserFactoryTests.cs
git commit -m "feat(core): ParserFactory builds an OpenAI-compatible IChatClient (OpenAI SDK <ver>, Microsoft.Extensions.AI.OpenAI <ver>, no retries) for a compatible candidate and reads its own <Vendor>:ApiKey; Agent:Parser accepts model, claude kept as a synonym; disclosure; ParserFactoryTests N passed"
```

---

### Task 3: The parser's request shape per provider, pacing, and the offline adapter tests

**Files:**
- Create: `src/Agent.Core/Parsing/ParsePacer.cs`
- Modify: `src/Agent.Core/Parsing/ClaudeParser.cs`
- Modify: `src/Agent.Core/Parsing/ParserFactory.cs` (register `ParsePacer` singleton in both branches that build a `ClaudeParser`)
- Modify: `tests/Agent.Core.Tests/Parsing/ClaudeParserTests.cs`, `AnthropicAdapterTests.cs`, `LiveModelTests.cs` (constructor call sites)
- Create: `tests/Agent.Core.Tests/Parsing/CompatibleAdapterTests.cs`

**Interfaces:**
- Produces: `public sealed class ParsePacer(TimeProvider clock)` with `public Task WaitAsync(string candidateName, int minMilliseconds, CancellationToken cancellationToken)`: returns at once when `minMilliseconds <= 0`; otherwise, under one lock per candidate name, computes the gap since that candidate's last call started and delays the remainder with `Task.Delay(gap, clock, cancellationToken)`, then records now as the last start. State: `ConcurrentDictionary<string, (SemaphoreSlim Gate, long LastStart)>`.
- `ClaudeParser` constructor becomes `(IChatClient client, IOptions<ModelOptions> options, ParsePacer pacer, TimeProvider clock, ITraceSink trace, ILogger<ClaudeParser> logger)`; `Name = candidate.Label + ":" + model.Candidate`.
- Produces: `public const string SchemaBlockPrefix = "Reply with exactly one JSON object that matches this schema.\n";` on `ClaudeParser`; `public static string SchemaBlock => SchemaBlockPrefix + Schema.GetRawText();`.
- Request shape:
  - `anthropic`: exactly as today.
  - `compatible`: system message is one plain `TextContent(Prompt)` (no cache control); in `json_object` mode a second `TextContent(SchemaBlock)` follows it in the same message; `ChatOptions` has `ModelId`, `MaxOutputTokens`, `Temperature`, no `RawRepresentationFactory`; `ResponseFormat` is `ForJsonSchema(Schema, "parse", description)` with `AdditionalProperties["strictJsonSchema"] = true` in `json_schema` mode, `ChatResponseFormat.Json` in `json_object` mode. If the adapter does not honour `strictJsonSchema` (the test below tells), set strict through `RawRepresentationFactory = _ => new ChatCompletionOptions { ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat("parse", BinaryData.FromString(Schema.GetRawText()), description, jsonSchemaIsStrict: true) }`.
  - Before the call, `await pacer.WaitAsync(model.Candidate, candidate.MinMillisecondsBetweenCalls, timeout.Token)`; the wait is inside the timed section so it counts in `Duration`.
- `ReasonOf` adds `System.ClientModel.ClientResultException api => "http_" + api.Status` before the `HttpRequestException` lines.
- `TokensOf` is unchanged: the `UsageDetails` branch already serves the compatible path.

- [ ] **Step 1: Write the failing tests.** In `ClaudeParserTests` (constructor sites updated to pass `new ParsePacer(TimeProvider.System)`; add an `OptionsCompatible(string outputMode = "json_schema", int paceMs = 0)` helper mirroring `ModelOptionsTests.Compatible()` but selected):

```csharp
[Fact] public async Task ACompatibleCandidateSendsNoCacheControlAndNoRawRequest()  // FakeChatClient records messages/options: system message is one TextContent without cache control (its AdditionalProperties / RawRepresentation null); options.RawRepresentationFactory is null; Name == "compatible:gemini"
[Fact] public async Task JsonObjectModeAddsTheSchemaBlockAndAsksForJson()           // system message has two TextContent; second == ClaudeParser.SchemaBlock; options.ResponseFormat is ChatResponseFormatJson with Schema null
[Fact] public async Task JsonSchemaModeSendsOnlyThePrompt()                         // one TextContent; ResponseFormat has the schema
[Fact] public async Task ACompatibleCandidateReadsUsageFromUsageDetails()           // FakeChatClient answers UsageDetails { InputTokenCount = 1200, CachedInputTokenCount = 1000, OutputTokenCount = 40 }: LastUsage == (200, 40, 0, 1000), CostUsd matches the prices
[Fact] public async Task PacingHoldsTheSecondCallBack()                             // paceMs = 150; two ParseAsync in a row; the second LastUsage.Duration >= 150 ms and the FakeChatClient's two call timestamps are >= 150 ms apart
[Fact] public async Task TwoParsersShareOnePacePerCandidate()                        // two ClaudeParser over the same ParsePacer and candidate name; a call on each; timestamps >= 150 ms apart
[Fact] public async Task PacingIsZeroByDefault()                                     // two calls under 100 ms total
```

In the new `CompatibleAdapterTests` (copy `AnthropicAdapterTests`' shape: `StubHandler` moved to a shared `internal sealed class RecordingHandler` in `FakeChatClient.cs` or kept as a copy, builder's choice; client from `ParserFactory.CreateCompatibleClient(FakeKey, candidate, new HttpClient(handler))`; a canned chat completion body with `choices[0].message.content` holding `{"requests":[<Item>]}` and `usage { prompt_tokens: 1200, completion_tokens: 40, prompt_tokens_details: { cached_tokens: 1000 } }`):

```csharp
[Fact] public async Task TheRequestIsStrictJsonSchemaWithThePromptAlone()  // body.model == ModelId; messages[0].role == "system" and its content is exactly Prompt (string or single text part); response_format.type == "json_schema"; response_format.json_schema.strict == true; json_schema.schema.additionalProperties == false; no "temperature"; no "cache_control" anywhere; no "output_config"; a max tokens field equal to MaxOutputTokens (max_completion_tokens)
[Fact] public async Task JsonObjectModeSendsTheSchemaBlockAndJsonObject()    // response_format.type == "json_object"; the system content holds Prompt followed by SchemaBlock
[Fact] public async Task TheReplyAndItsUsageAreRead()                        // intent bill; LastUsage == (200, 40, 0, 1000)
[Fact] public async Task AnOffSchemaJsonObjectReplyIsTheJsonProblem()        // content {"requests":[{"intent":"bill","extra":1}]} and content {"ok":true}: Problem == "json"
[Fact] public async Task ALengthStopIsTruncated()                            // finish_reason "length"
[Fact] public async Task AContentFilterStopIsTheRefusal()                    // finish_reason "content_filter"
[Fact] public async Task AnHttpFailureIsUnavailableWithoutTheKeyOrTheBody()  // 400 with a body containing "max_completion_tokens is not supported" and 429: ParserUnavailableException, message "The model did not answer (http_400)." / "(http_429)"; message contains neither FakeKey nor the body text; the request carried header Authorization: Bearer FakeKey (so the key went where it should and nowhere else)
[Fact] public async Task TheOutboundTextIsMasked()                            // a message with a phone number: the body holds the token, not the digits (same check as OutboundPayloadTests' idea, on the wire)
```

- [ ] **Step 2: Run** both classes. Expected: build failure (`ParsePacer`, constructor, `SchemaBlock`).

- [ ] **Step 3: Implement** `ParsePacer`, the parser changes and the DI registration.

- [ ] **Step 4: Run** `ClaudeParserTests`, `CompatibleAdapterTests`, `AnthropicAdapterTests`, `ParserFactoryTests`, then `dotnet test tests/Agent.Core.Tests`. Expected: all PASS (the `LiveModelTests` skip without the gate). If `strictJsonSchema` was ignored, switch to the raw factory and rerun.

- [ ] **Step 5: Commit**

```bash
git add src/Agent.Core/Parsing tests/Agent.Core.Tests/Parsing
git commit -m "feat(core): the parser shapes the call by provider (compatible: plain system prompt, strict json_schema or json_object with the schema block, no effort, usage from UsageDetails), paces calls through one ParsePacer per candidate, maps ClientResultException to http_NNN; CompatibleAdapterTests N, ClaudeParserTests N, AnthropicAdapterTests N passed"
```

---

### Task 4: The runner: `compatible:<candidate>`, the key copy rule, the report header

**Files:**
- Modify: `src/Agent.Eval/Running/EvalArguments.cs`
- Modify: `src/Agent.Eval/Running/EvalCommand.cs:52-66` (header) and `:353-367` (key copy)
- Test: `tests/Agent.Core.Tests/Eval/HeldOutGuardTests.cs`

**Interfaces:**
- Consumes: `ModelOptions.IsVendorKeySetting`, `ModelCandidate.Label`, `IsCompatible`, `OutputMode`, `MinMillisecondsBetweenCalls`; `ParserFactory.Model`.
- Produces on `EvalArguments`: `public const string CompatiblePrefix = "compatible:";`; `Candidate` returns the name after either prefix; `public string? Label` (`claude` or `compatible`, null for scripted); the pattern `^(scripted|claude:[a-z0-9_-]+|compatible:[a-z0-9_-]+)$`; the error text `--parser needs 'scripted', 'claude:<candidate>' or 'compatible:<candidate>', a candidate of the Model section.`
- `EvalCommand.BuildHost`: sets `Agent:Parser` to `model` when a candidate is named; reads `Model:Candidates:<name>:Provider` and `:ApiKeySetting` from `builder.Configuration`; if the provider's label is not the prefix's label, writes the usage error `--parser <prefix> names '<name>', whose provider is '<provider>'.` and returns the usage exit code (builder: the same path as a `Parse` error); for `anthropic` the copy is unchanged; for `compatible`, when the runner's own configuration has no value under `ApiKeySetting` and `IsVendorKeySetting(ApiKeySetting)` is true, copies that one value from the web user secrets; otherwise copies nothing.
- `EvalCommand.ModelSettings` adds `Model:Provider` (the provider), `Model:OutputMode` (`json_schema`/`json_object` for `compatible`, `not sent` for `anthropic`), `Model:MinMillisecondsBetweenCalls`; for `compatible`, `Model:Effort` and `Model:PromptCacheTtl` are `not sent` (add `Model:PromptCacheTtl` for both providers; the Anthropic value is the candidate's TTL). The parser name in the report file name needs no change (`compatible:gemini` becomes `compatible-gemini` in `ReportParserName`).

- [ ] **Step 1: Write the failing tests** in `HeldOutGuardTests` (next to the existing `--parser` theories):

```csharp
[InlineData(new[] { "dev", "--parser", "compatible:gemini" }, "compatible:gemini")]   // added to the existing accepted-parser theory
[Theory] [InlineData("compatible:")] [InlineData("gemini")] [InlineData("model:gemini")]
public void AParserWithoutAKnownPrefixIsRefused(string parser)
[Fact] public void TheCandidateAndLabelFollowThePrefix()                               // "compatible:gemini" gives Candidate "gemini", Label "compatible"; "claude:haiku" gives "haiku", "claude"; scripted gives null, null
[Fact] public void TheHeaderOfACompatibleCandidateSaysProviderModeAndPacing()          // ModelSettings("gemini", compatible): Model:Provider == "compatible", Model:OutputMode == "json_schema", Model:Effort == "not sent", Model:PromptCacheTtl == "not sent", Model:MinMillisecondsBetweenCalls == "0"
[Fact] public void TheHeaderOfAnAnthropicCandidateIsUnchangedPlusProviderAndTtl()      // the existing assertions plus Model:Provider == "anthropic", Model:PromptCacheTtl == "5m", Model:OutputMode == "not sent"
[Fact] public void TheRunnerCopiesOnlyAVendorKeyShapedSetting()                         // builder: expose the copy decision as `public static string? SecretToCopy(IConfiguration runnerConfiguration, string candidateName)` on EvalCommand returning the setting name to copy or null; assert "Gemini:ApiKey" for the gemini candidate, null when the runner already has it, null when ApiKeySetting is "ConnectionStrings:Agent", "Anthropic:ApiKey" for haiku
[Fact] public void APrefixThatDoesNotMatchTheProviderIsAUsageError()                   // builder: expose `public static string? PrefixMismatch(IConfiguration configuration, EvalArguments parsed)`; "claude:gemini" over a config where gemini is compatible gives the message; "compatible:gemini" gives null
```

- [ ] **Step 2: Run** `dotnet test tests/Agent.Core.Tests -- --filter-class "Agent.Core.Tests.Eval.HeldOutGuardTests"`. Expected: build failure.

- [ ] **Step 3: Implement** the Interfaces block.

- [ ] **Step 4: Run** `HeldOutGuardTests`, `ReportWriterTests`, `RunnerSmokeTests`, then `dotnet test tests/Agent.Core.Tests`. Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Agent.Eval/Running tests/Agent.Core.Tests/Eval/HeldOutGuardTests.cs
git commit -m "feat(eval): --parser compatible:<candidate>; the prefix must match the candidate's provider; the runner copies only a <Vendor>:ApiKey setting from the console's secrets; the report header carries Model:Provider, Model:OutputMode, Model:PromptCacheTtl and Model:MinMillisecondsBetweenCalls; HeldOutGuardTests N passed"
```

---

### Task 5: Configuration, settings panel, the gated smoke test, docs

**Files:**
- Modify: `src/Agent.Web/appsettings.json:11-51`, `src/Agent.Eval/appsettings.json` (the same `Model` section)
- Modify: `src/Agent.Web/Components/Console/SettingsPanel.razor:287-307`
- Modify: `tests/Agent.Core.Tests/Web/RuntimeSettingsTests.cs` (if it lists Model rows; builder checks)
- Create: `tests/Agent.Core.Tests/Parsing/LiveCompatibleTests.cs`
- Modify: `docs/agent-model.md`, `docs/architecture.md:71,250`, `docs/failure-modes.md`, `docs/disclosure.md` (Models list), `CLAUDE.md` (the "Model parser" bullet)

**Interfaces:**
- Consumes: everything above; `DevSetText.SidecarText(id, step, request, field)` and `DevSetText.Say(id, step)` for the oracle's expected values; `EvalCommand.WebUserSecretsId`.
- Config entries (both files), exact values:

```json
"gemini": {
  "Provider": "compatible",
  "BaseUrl": "https://generativelanguage.googleapis.com/v1beta/openai",
  "ApiKeySetting": "Gemini:ApiKey",
  "ModelId": "",
  "OutputMode": "json_schema",
  "MaxOutputTokens": 1500,
  "TimeoutSeconds": 30,
  "MinMillisecondsBetweenCalls": 4000,
  "InputPricePerMTok": 0, "OutputPricePerMTok": 0, "CacheWritePricePerMTok": 0, "CacheReadPricePerMTok": 0
},
"openrouter": {
  "Provider": "compatible",
  "BaseUrl": "https://openrouter.ai/api/v1",
  "ApiKeySetting": "OpenRouter:ApiKey",
  "ModelId": "",
  "OutputMode": "json_schema",
  "MaxOutputTokens": 1500,
  "TimeoutSeconds": 30,
  "MinMillisecondsBetweenCalls": 3000,
  "InputPricePerMTok": 0, "OutputPricePerMTok": 0, "CacheWritePricePerMTok": 0, "CacheReadPricePerMTok": 0
}
```

  (`ModelId` empty on purpose: Khalil fills it from the console; the start fails only when that candidate is selected. The pacing values are my guess at free-tier minute limits; Khalil tunes them.)
- Settings panel rows per candidate: `ModelId` row shows `provider + " " + ModelId` as today; add `BaseUrl` (compatible only), `ApiKeySetting` (compatible only, the name, never a value), `OutputMode` (compatible only), `MinMillisecondsBetweenCalls` (every candidate); `Effort` and `PromptCacheTtl` rows show `not sent` for `compatible`.
- `LiveCompatibleTests(ITestOutputHelper output)`: gated by `ZAINCASH_MODEL_TESTS=1`; builds configuration as `LiveModelTests.Parser` does; skips unless the selected `Model:Candidate` is `compatible` and its key is present; one theory over four development case ids chosen by the builder, one per intent (a transfer, a bill, a purchase, a balance question), each with a sidecar parse line; masks `DevSetText.Say(id)` with `Masker.Mask`, parses, and asserts the intent and `recipient_text`/`biller_code`/`product_type` equal the sidecar's values for request 0; prints usage. No literal Arabic and no hand-written expected value.

- [ ] **Step 1: Config and panel.** Add the two entries to both files; add the rows. Run `dotnet build` and `RuntimeSettingsTests`. Expected: PASS. Run `dotnet run --project src/Agent.Web` and open Settings: the two candidates show with their address and key setting name, and no key value appears anywhere on the page.

- [ ] **Step 2: Write `LiveCompatibleTests`** as above. Run `dotnet test tests/Agent.Core.Tests -- --filter-class "Agent.Core.Tests.Parsing.LiveCompatibleTests"` without the gate. Expected: Skipped (4 skips), not failed.

- [ ] **Step 3: Docs.**
  - `docs/agent-model.md`, "Candidates and switching": the `compatible` provider, its five fields with the table from the spec, output modes, the strict flag, usage and cost for `compatible` (no cache writes), the key commands (`dotnet user-secrets set "Gemini:ApiKey" "<key>" --project src/Agent.Web`, same for `OpenRouter:ApiKey`), `Agent:Parser` `model` with `claude` as synonym, the report header's new entries, the pacing setting, and a "Comparison procedure" subsection: smoke test first, flip to `json_object` on a schema rejection, then `dev --parser compatible:<candidate> --repeat 3`, read the aggregate header, apply D13. Record that the thinking-control setting was offered and not taken up.
  - `docs/architecture.md`: line 71 node text `LLM provider<br/>one of the configured candidates`; line 250 row: `One provider-neutral chat interface (Microsoft.Extensions.AI) over the Anthropic SDK or the OpenAI SDK pointed at a compatible vendor`.
  - `docs/failure-modes.md`: two rows (a compatible vendor answers prose or off-schema JSON: `json` problem, no card; a free-tier rate limit: `http_429`, handover reply, counted in the run, pacing setting as the mitigation).
  - `docs/disclosure.md`, Models: `Google Gemini through Google AI Studio's OpenAI-compatible endpoint, candidate gemini (model id set when first run; the free tier may use prompts for training; the prompt carries masked user text and the pending note only)` and the same line for OpenRouter.
  - `CLAUDE.md`, the "Model parser" bullet: `Agent:Parser` is `scripted`, `model` (or its synonym `claude`); the key command per vendor; `--parser compatible:<candidate>`; the plan file name.

- [ ] **Step 4: Run** `dotnet build`, `dotnet test tests/Agent.Core.Tests`, `dotnet test tests/Agent.Web.Journeys`. Expected: all PASS or Skipped, none failed.

- [ ] **Step 5: Commit**

```bash
git add src/Agent.Web/appsettings.json src/Agent.Eval/appsettings.json src/Agent.Web/Components/Console/SettingsPanel.razor tests/Agent.Core.Tests/Parsing/LiveCompatibleTests.cs tests/Agent.Core.Tests/Web docs CLAUDE.md
git commit -m "feat(config): the gemini and openrouter candidates (compatible provider, model id to fill, paced), shown on the settings panel without a key; gated LiveCompatibleTests over the oracle's parse lines; agent-model.md, architecture.md, failure-modes.md, disclosure.md, CLAUDE.md; Agent.Core.Tests N passed, journeys N passed"
```

---

### After the plan (Khalil, not the implementer)

1. `dotnet user-secrets set "Gemini:ApiKey" "<key>" --project src/Agent.Web` and the same for `OpenRouter:ApiKey`.
2. Fill `ModelId` for `gemini` (a Flash or Flash-Lite id from AI Studio) and `openrouter` (a `vendor/model:free` id), and the prices if any.
3. `$env:ZAINCASH_MODEL_TESTS='1'` and run `LiveCompatibleTests` with `Model:Candidate` set in user secrets. On a schema rejection, set that entry's `OutputMode` to `json_object`.
4. `dotnet run --project src/Agent.Eval -- dev --parser compatible:gemini --repeat 3`, then the same for `openrouter`; compare the aggregate headers with the latest Haiku run under D13.
