# Design: other model providers for the comparison

Date: 28 Oct 2026. Author: Claude (Fable 5.1), from a conversation with Khalil. Status: approved by Khalil, 28 Oct 2026 (with Gemini and OpenRouter in place of Grok).

## Intent

The architecture's model selection rule (D13) picks the candidate with the fewest wrong cards on the development set, and on a close call the cheaper or in-country one. Until now every candidate has been an Anthropic model, so the rule has only been applied within one vendor. Khalil wants to widen the comparison with two free-tier doors he already holds keys for (28 Oct 2026): Google AI Studio (Gemini) and OpenRouter (one key, many models, the free ones ending `:free`, which is where Qwen, DeepSeek or GLM would come from). Grok was considered and dropped. No local model: a hosted API is simpler. The question to answer is whether either gives output as good as the paid Anthropic candidate at a lower cost.

Switching must stay a one-line change in `appsettings.json`: `Model:Candidate` names the entry, and every entry carries everything it needs (address, model id, key setting, prices, mode).

The runner already grades every case and prints the cost of a run, so once another provider can be configured, the comparison is one `dev` run per candidate and a table of pass count against cost.

## Goals

- A candidate from any vendor whose API speaks the OpenAI chat completions format is a configuration entry: base address, model id, where its key lives, prices.
- The same prompt, the same masking, the same schema, the same grading. Nothing about a run changes except the model answering.
- Every report says which vendor, model and output mode it ran with, so reports from different vendors sit side by side in `docs/reports/` and are comparable.
- The Anthropic path keeps working exactly as it does: same settings, same report names, same tests.

## Non-goals

- No local model serving (Ollama, LM Studio). The provider would accept a localhost address in principle, but nothing is built or tested for it.
- No comparison screen and no aggregate report across candidates. The table is made by hand from the report headers.
- No change to the selection rule, the prompt file, the answer keys or the matching rules.
- No streaming, no tools, no vision: the parse call stays one request, one strict reply.

## Anti-goals

- A vendor-specific branch in the conversation, the resolver or the grader. The only code that knows the vendor is the client factory and the request shape.
- A key in a file, a log, a trace or a report. Each vendor's key stays in user secrets or an environment variable, as the Anthropic key does.
- A provider that quietly changes what the model sees. If a vendor needs the schema in the prompt (see output modes), the report says so.

## Constraints (from CLAUDE.md and the architecture)

- Rule 2: the model is read only, one parse call per message, strict structure validated by code. Rule 5: masking before the text leaves. Rule 9: no secrets in the repository. Unchanged.
- The frozen prompt `parse-prompt.md` is at version 6 and is not edited for this work.
- Every new library and every vendor or model used goes in `docs/disclosure.md` in the same commit.
- Limits and settings are configuration, shown read-only on the settings panel.
- Tests are xUnit; logging is Microsoft.Extensions.Logging.

## The design

### One new provider: `compatible`

`ModelCandidate.Provider` accepts `anthropic` (as today) or `compatible`: a vendor whose API accepts the OpenAI chat completions request at a base address. That one provider covers Google's Gemini (its OpenAI-compatible endpoint), OpenRouter, xAI, DeepSeek, Alibaba's Model Studio (Qwen), Z.ai / Zhipu (GLM) and OpenAI itself.

A `compatible` candidate carries, in addition to the fields every candidate has (`ModelId`, `MaxOutputTokens`, `TimeoutSeconds`, `Temperature`, the four prices):

| Field | Meaning |
|---|---|
| `BaseUrl` | The vendor's API root, e.g. `https://openrouter.ai/api/v1`. Required, absolute, `https`. |
| `ApiKeySetting` | The configuration key that holds the vendor's key, in the form `<Vendor>:ApiKey` (letters and digits, then `:ApiKey`; nothing else is accepted), e.g. `Gemini:ApiKey`. Required. Set with `dotnet user-secrets set "Gemini:ApiKey" "<key>" --project src/Agent.Web`, or as the environment variable `Gemini__ApiKey` (configuration's own mapping). The value is never shown. |
| `OutputMode` | `json_schema` (the default) or `json_object`. See below. |
| `RequestFields` | Optional, `compatible` only (29 Oct 2026): the text of a JSON object whose top-level members are added as given to the top level of the request body, for a vendor's own field, e.g. `{"reasoning":{"effort":"low"}}` on `openrouter`. Must be an object, may not name `model`, `messages`, `response_format`, `max_completion_tokens`, `max_tokens`, `temperature`, `stream`, `tools` or `tool_choice`, and no member name may contain a quote or a backslash; empty or `{}` sends nothing. Shown on the settings panel and recorded in the report header (`Model:RequestFields`). In `appsettings.json` it is a string, so its quotes are escaped: `"RequestFields": "{\"reasoning\":{\"effort\":\"low\"}}"`. |
| `MinMillisecondsBetweenCalls` | Pacing for rate-limited free tiers: the least gap between two calls to this candidate, in milliseconds, default 0 (Khalil, 28 Oct 2026). Enforced by the parser with one gate per candidate, so the console and the runner both honour it; a wait counts in the call's duration. Shown on the settings panel and recorded in the report header. Accepted on `anthropic` candidates too, default 0, so nothing changes for them. |

`Effort` and `PromptCacheTtl` are Anthropic-only. For a `compatible` candidate they are not sent and not validated; the report header shows `not sent` for both.

**Thinking models.** The code builds no vendor-specific request field of its own (no `enable_thinking`, no `max_tokens` alias); a vendor's field goes in the candidate's `RequestFields`. That setting, proposed on 28 Oct 2026, was taken up on 29 Oct 2026 (Khalil) after the first live run of `recursive/flint-v1.0:free` through OpenRouter: that model's reasoning cannot be switched off and ran to 3,000 to 12,000+ output tokens a parse (60 to 80 s, usually cut off at `MaxOutputTokens`, so `truncated`), while OpenRouter's own field `"reasoning": {"effort": "low"}` brought it to about 180 completion tokens in 2 s. The shape: one string per candidate holding the text of a JSON object, parsed at start; every top-level member is added, with its value as given, to the top level of the chat-completions body. It is `compatible`-only (an `anthropic` candidate that sets it is refused at start), it must be a JSON object, and it may not name a field the parser sets itself (`model`, `messages`, `response_format`, `max_completion_tokens`, `max_tokens`, `temperature`, `stream`, `tools`, `tool_choice`, compared ordinally), and no member name may contain a quote or a backslash; empty or `{}` sends nothing. It is static configuration, opaque to the code, never model or wallet text, and changes neither message (rules 2 and 5). Thinking that the vendor offers no control for still counts against `MaxOutputTokens`: raise the limit, or do not use the model. On OpenRouter, prefer a model id that does not think by default.

The client: the official `OpenAI` .NET SDK's `OpenAIClient` with the key and `OpenAIClientOptions.Endpoint = BaseUrl`, no retries (as the Anthropic client: one call per message), then `GetChatClient(ModelId).AsIChatClient()` through `Microsoft.Extensions.AI.OpenAI`. Both packages are new and go in the disclosure.

### Output modes

The parser asks for structured output through `ChatOptions.ResponseFormat = ForJsonSchema(schema)`, which the OpenAI adapter sends as `response_format: { type: "json_schema", ... }`. Gemini's OpenAI-compatible endpoint, OpenAI and OpenRouter (for models that support it) accept that. Some vendors and some OpenRouter models accept only `json_object` (check on the day). For those, `OutputMode: json_object`:

- the request carries `response_format: { type: "json_object" }`;
- the system message is the frozen prompt followed by the schema block in one system string (a line break, a fixed English sentence, "Reply with exactly one JSON object that matches this schema.", and the schema JSON the parser already builds), so it goes out as a string, which every OpenAI-compatible server takes on the system role (controller ruling of 28 Oct 2026, after the final review). The frozen prompt file is unchanged; the block is assembled in code from the same schema the `json_schema` mode sends;
- the reply is validated by the same strict validator as every other reply; a reply that is not the structure is the existing `json` problem.

The report header records `Model:OutputMode`, so a comparison across modes is visible.

In `json_schema` mode the request says `strict: true` (without it the mode is a hint, not a constraint). No schema is transformed per vendor: the schema sent is the one the parser builds, for every vendor. A vendor that rejects it answers HTTP 400, which is the existing `ParserUnavailableException`, so the first case of a run shows it at once; the remedy is to set that candidate to `json_object` by hand, never an automatic fallback, so a report's mode is always the one configured.

### Usage and cost

`compatible` candidates report usage through `UsageDetails` only: `InputTokenCount` is the whole input, `CachedInputTokenCount` the part served from the vendor's automatic cache (OpenAI's `prompt_tokens_details.cached_tokens`; Gemini and OpenRouter report the same field where the model caches). There is no cache write on these APIs, so the cost is `(input - cached) x InputPrice + cached x CacheReadPrice + output x OutputPrice`, which is the existing `CostUsd` with `cacheWrite = 0`. `CacheWritePricePerMTok` is set to 0 for these candidates. The report's token and cache columns keep their meaning.

The estimate printed before a run uses the same formula as today with the candidate's prices.

### Names

- `IParser.Name` becomes `<label>:<candidate>`, where the label is `claude` for `anthropic` (unchanged, so existing report names and docs stay right) and `compatible` for the new provider. A Gemini run writes `dev-compatible-gemini-<date>-<HHmm>`.
- `Agent:Parser` gains the value `model`, meaning the model parser over the candidate `Model:Candidate` names, whatever its provider. `claude` stays accepted and means the same thing (so existing secrets and docs keep working).
- `Agent.Eval --parser` accepts `scripted`, `claude:<candidate>` or `compatible:<candidate>`; the prefix must match the candidate's provider, else the usage error names the mismatch.

### The runner's copy of the key

`Agent.Eval` reads the web console's user secrets and copies only `Anthropic:ApiKey` into its own configuration, on purpose: the console's other secrets (its agent database string above all) must never reach the runner, which empties the tables of the database it uses. That rule is kept and widened by exactly one pattern: the runner copies the selected candidate's `ApiKeySetting` only when it matches `<Vendor>:ApiKey`, which the validation above already requires. A setting of any other shape is refused at start with a message that names the setting. Nothing else is copied.

### Validation at start (with `model` or `claude`)

Everything `ModelOptions.Problems()` checks today, plus for `compatible`: `BaseUrl` absolute and `https`; `ApiKeySetting` set; `OutputMode` one of the two; and the key present under `ApiKeySetting` (the message names the setting, never a value). For `anthropic` nothing changes, including the `Anthropic:ApiKey` / `ANTHROPIC_API_KEY` key.

### Refusals, errors, timeouts

Same as the Anthropic path: `ChatFinishReason.ContentFilter` is the `refusal` problem; a cut-off reply is `truncated`; a non-JSON or off-schema reply is `json`; an HTTP failure or a timeout is `ParserUnavailableException`, which the conversation turns into the handover reply. The error message and the log never include the key or the vendor's response body.

### Configuration shipped in the repository

`src/Agent.Web/appsettings.json` and `src/Agent.Eval/appsettings.json` gain two `compatible` entries with the base address and key setting filled and the model id and prices left for Khalil to fill from the console on the day (ids and prices change; the report header records what was used):

| Candidate | BaseUrl | ApiKeySetting | Notes |
|---|---|---|---|
| `gemini` | `https://generativelanguage.googleapis.com/v1beta/openai` | `Gemini:ApiKey` | Google AI Studio's free tier through its OpenAI-compatible endpoint; json_schema; pacing set, since the free tier is rate-limited per minute. Prefer a Flash or Flash-Lite id; a model that thinks by default needs a higher `MaxOutputTokens` |
| `openrouter` | `https://openrouter.ai/api/v1` | `OpenRouter:ApiKey` | One key, many models; the model id is OpenRouter's (`vendor/model`, free ones end `:free`); OutputMode per the chosen model; pacing set, since free models are rate-limited |

A second OpenRouter model is a copy of the entry under another name (`openrouter-qwen`, `openrouter-glm`), so several can be configured at once and `Model:Candidate` picks one. A candidate whose model id is empty fails the start only when it is the one selected, as today. Entries for other doors (xAI `https://api.x.ai/v1`, DeepSeek `https://api.deepseek.com/v1`, Alibaba Model Studio `https://dashscope-intl.aliyuncs.com/compatible-mode/v1`, Z.ai `https://api.z.ai/api/paas/v4`) are not shipped; they are the same shape if wanted later.

### Tests

- `ModelOptionsTests`: the new validation lines, both providers, including `MinMillisecondsBetweenCalls` not below 0.
- `ClaudeParserTests`: with a fake clock, two calls on a paced candidate are at least the gap apart, and the gap is 0 by default.
- `ParserFactoryTests`: `compatible` builds an `IChatClient` against the base address; the key is read from `ApiKeySetting`; a missing key fails the start with a message that names the setting.
- `CompatibleAdapterTests`, the twin of `AnthropicAdapterTests`: over the real OpenAI adapter with the HTTP answered by a stub, it reads the request body and proves `response_format` is `json_schema` or `json_object` by mode, the schema block is present only in `json_object` mode, no `temperature` is sent when unset, no cache control and no effort appear; the reply and its usage are read; an HTTP failure becomes `ParserUnavailableException` without the key in the message.
- `EvalArgumentsTests`: the new prefix and the provider mismatch error.
- `OutboundPayloadTests` (masking on the wire) runs over both adapters: its three theories play the whole conversation with the Anthropic candidate and with a `compatible` one in each output mode, over the fake chat client, so the seed-derived checks cover every part of the system message each provider sends; the real adapter's wire body is read by `CompatibleAdapterTests.TheOutboundTextIsMasked`.
- `LiveModelTests` are untouched: their theories over haiku, sonnet and opus, with their expected rows, stay as they are (those rows are close to answer keys, which only Khalil sets). A new gated class, `LiveCompatibleTests`, runs on the `compatible` candidate `Model:Candidate` names, over a handful of clean development cases (CLN-01 and a bill, a purchase and a balance case), and expects the intent and slots the scripted parser's oracle line in `data/requests.dev.parses.jsonl` holds for that case. No expected value is written by hand. It skips when the selected candidate is not `compatible`, and it is gated by the same `ZAINCASH_MODEL_TESTS` variable. It is the first thing to run on Gemini and on each OpenRouter model: if it fails on the output mode, flip the entry to `json_object` before a full run.
- `PhoneStringsTests` untouched: no new user-facing string. The handover reply already exists.

### Documentation

- `docs/agent-model.md`: the candidates table, the `compatible` fields, output modes, cost, the key commands, the comparison procedure.
- `docs/architecture.md`: the LLM node says "one of the configured candidates"; the stack row for model access names both adapters. D13 is unchanged.
- `docs/disclosure.md`: `OpenAI` SDK, `Microsoft.Extensions.AI.OpenAI`, and one line per vendor and model actually run, added when its first report is committed, with a note on the vendor's data-use terms where a free tier is tied to training on prompts (Google AI Studio's free tier and OpenRouter's free models both say prompts may be used that way; the prompt carries only masked user text and the pending note).
- `docs/failure-modes.md`: a vendor that ignores the output mode and answers prose (handled: `json` problem, no card); a vendor rate limit on a free tier (handled: `model_unavailable` handover, the run's report counts it).
- `CLAUDE.md`: the parser values and the key command for a `compatible` candidate.

### The comparison procedure (for Khalil, after the build)

1. Set the key, fill the model id and prices, set `Model:Candidate`.
2. `dotnet run --project src/Agent.Eval -- dev --parser compatible:<candidate> --repeat 3` (the Anthropic baselines were single runs plus `--repeat` where variance mattered; three runs give a median).
3. Read passes, cost and time from the aggregate report header. Apply D13.

## Calls made in this design (Claude's, for Khalil to overturn)

1. One generic `compatible` provider rather than one provider per vendor.
2. The key setting name is per candidate (`ApiKeySetting`) rather than one fixed `Compatible:ApiKey`, so two vendors can be configured at once.
3. `json_object` mode puts the schema in a second system block built from the same schema object; the prompt file is not touched.
4. Parser label `compatible` in report names; Anthropic reports keep `claude`.
5. `Agent:Parser` value `model` added, `claude` kept as a synonym.
6. `https` required for `BaseUrl`.
7. Two entries shipped, `gemini` and `openrouter` (Khalil holds both keys; Grok dropped); the vendors' own consoles are not.
8. `strict: true` in `json_schema` mode; no per-vendor schema transform; a rejected schema is a start-of-run failure, and the switch to `json_object` is made by hand.
9. The runner copies a vendor key only under the `<Vendor>:ApiKey` shape (the builder check raised this; the console's other secrets stay out of the runner).
10. The Anthropic live tests are untouched; a `compatible` candidate gets its own gated smoke test against the oracle's parse lines.
11. No vendor-specific request field is built into the code; the `RequestFields` setting, offered on 28 Oct 2026, was taken up on 29 Oct 2026 after the Flint run: a JSON object per `compatible` candidate, its top-level members added to the body as given, none of the fields the parser sets (`model`, `messages`, `response_format`, `max_completion_tokens`, `max_tokens`, `temperature`, `stream`, `tools`, `tool_choice`), no member name with a quote or a backslash, refused on an `anthropic` candidate. A thinking model with no vendor control is used with a higher output limit or not at all.
12. Pacing is enforced in the parser, one gate per candidate, not in the runner, so a case's several calls are all paced.

## Left to the builder

- Which `OpenAI` and `Microsoft.Extensions.AI.OpenAI` versions (current stable, matching `Microsoft.Extensions.AI` 10.11.0).
- How the stub handler for `CompatibleAdapterTests` is shared with `AnthropicAdapterTests`.
- Whether the settings panel needs a layout change to show the three new fields (it shows the section read-only; the key setting's name is shown, its value never).
- Commit slicing. One suggested order: options and validation; factory and client; parser request shape and usage; eval arguments and report header; docs and disclosure.
