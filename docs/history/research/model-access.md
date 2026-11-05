# Research: model access from .NET and Iraqi Arabic speech input

Research only. No repository files were read or changed. Sources were fetched on 2026-10-14. Items marked UNVERIFIED were not confirmed by a fetched official page.

## Part A: calling Claude from .NET through Microsoft.Extensions.AI

### 1. Official SDK and IChatClient
- Official package: NuGet `Anthropic`, repository https://github.com/anthropics/anthropic-sdk-csharp. Install with `dotnet add package Anthropic`. Latest stable shown on the NuGet page at fetch time: 12.55.0 (published/last updated 10/9/2026). Source: https://www.nuget.org/packages/Anthropic and https://github.com/anthropics/anthropic-sdk-csharp
- The official SDK already implements `IChatClient`: `client.AsIChatClient("claude-opus-5-5")`. No adapter package is needed. Source: https://platform.claude.com/docs/en/cli-sdks-libraries/sdks/csharp (section "IChatClient integration")
- The community `tryAGI.Anthropic` package (3.x line, separate project, not affiliated with Anthropic) is no longer needed. Source: https://platform.claude.com/docs/en/cli-sdks-libraries/sdks/csharp (Warning block)
- The C# SDK page does not state a beta status; the German version of the page did, so maturity is unclear. Source: https://platform.claude.com/docs/en/cli-sdks-libraries/sdks/csharp

### 2. Schema-conforming output
- Recommended: structured outputs. Set `output_config.format` with `"type": "json_schema"` and a `schema` (use `additionalProperties: false` and `required`). No beta header is needed for the `output_config` shape. Source: https://platform.claude.com/docs/en/build-with-claude/structured-outputs
- Strict tool use (`strict: true` on a tool) is a separate feature that validates tool calls. The docs say it can be combined with `output_config.format`, but it is not a substitute for it. Source: https://platform.claude.com/docs/en/build-with-claude/structured-outputs
- Forced tool calls are a problem on this project's likely models. `tool_choice` `any` or `tool` returns a 400 on Claude Opus 5.5, Claude Sonnet 5.5 and Claude Fable 5.1. So `ToolMode.RequireSpecific` would fail there. Source: https://platform.claude.com/docs/en/agents-and-tools/tool-use/implement-tool-use (Forcing tool use table)
- Use `output_config.format` for the one parse call. Raw C# form: `Messages.Create<T>` with a class, or `OutputConfig` with `JsonOutputFormat`. Source: https://platform.claude.com/docs/en/build-with-claude/structured-outputs (SDK helpers)
- MEAI equivalent: `ChatOptions.ResponseFormat = ChatResponseFormat.ForJsonSchema(...)`. The API page confirms this shape and that null means no format is set. Source: https://learn.microsoft.com/en-us/dotnet/ai/microsoft-extensions-ai (search result: ChatResponseFormat API, not fetched in full)
- UNVERIFIED: whether the Anthropic `AsIChatClient` maps `ChatOptions.ResponseFormat` onto `output_config.format`. Check the SDK source or a test before relying on it. Also UNVERIFIED: `ToolMode.RequireSpecific` naming, since the search did not return it.

### 3. Model IDs and prices (per million tokens, US$)
- IDs (exact): `claude-fable-5-1`, `claude-opus-5-5`, `claude-sonnet-5-5`, `claude-haiku-5-5`. No date suffix. Source: https://platform.claude.com/docs/en/models/overview
- Fable 5.1: input $10, output $50. Opus 5.5: input $4, output $20. Sonnet 5.5: input $2, output $10. Haiku 5.5: input $0.10, output $0.50 (up to 100K-token prompts; $0.50 / $2.50 above 100K). Source: https://platform.claude.com/docs/en/about-claude/pricing
- Cache reads: Fable 5.1 $0.25; Opus 5.5 $0.20; Sonnet 5.5 $0.10; Haiku 5.5 $0.01. Cache writes: 5-minute 1.25x base input, 1-hour 2x base. Source: https://platform.claude.com/docs/en/about-claude/pricing and https://platform.claude.com/docs/en/build-with-claude/prompt-caching
- Discrepancy: the claude-api skill's cached table gives Sonnet 5.5 cache reads at $0.20. The official pricing page says $0.10. Use the official figure.
- Smaller model: the models overview positions Haiku 5.5 for "high-volume, latency-sensitive tasks such as classification, extraction, and routing", and calls it the fastest. Source: https://platform.claude.com/docs/en/models/overview
- Caching: minimum cacheable prompt is 512 tokens for Haiku 5.5, Opus 5.5 and Sonnet 5.5. Shorter prompts are not cached and no error is returned. Batch API is 50% off. Source: https://platform.claude.com/docs/en/build-with-claude/prompt-caching and https://platform.claude.com/docs/en/about-claude/pricing
- Short system prompt: a system prompt under 512 tokens gets no caching benefit on these models, so caching does not help a very short parse prompt. Source: https://platform.claude.com/docs/en/build-with-claude/prompt-caching

### 4. Token counts and latency
- Anthropic response usage: `usage.input_tokens` and `usage.output_tokens` on each message. Source: https://platform.claude.com/docs/en/build-with-claude/working-with-messages
- Through MEAI: `ChatResponse.Usage` is a nullable `UsageDetails`. Source: https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.chatresponse.usage
- UNVERIFIED: the `UsageDetails` member names (`InputTokenCount`, `OutputTokenCount`) were not fetched. Also UNVERIFIED: that the Anthropic `AsIChatClient` populates `Usage`, including cache tokens.
- Latency: the Anthropic C# SDK does not expose request timing. It exposes `WithRawResponse` (status code and headers) and `Timeout`, so time each call yourself, for example with `Stopwatch`. Source: https://platform.claude.com/docs/en/cli-sdks-libraries/sdks/csharp (Raw responses, Timeouts)
- Per-case cost: (input_tokens x input price + output_tokens x output price) / 1,000,000, plus cache terms if used. Source: https://platform.claude.com/docs/en/about-claude/pricing

### 5. Minimal setup
- `dotnet add package Anthropic`
- `dotnet add package Microsoft.Extensions.AI` (provides `AddChatClient`). Source: https://learn.microsoft.com/en-us/dotnet/ai/microsoft-extensions-ai
- Registration: `builder.Services.AddChatClient(new AnthropicClient { ApiKey = key }.AsIChatClient("claude-haiku-5-5"));`. The DI pattern `AddChatClient(IChatClient)` is shown at https://learn.microsoft.com/en-us/dotnet/ai/ichatclient (Dependency injection section, with another provider).
- Keys: the SDK reads `ANTHROPIC_API_KEY` from the environment by default. It does not read a config key named `Anthropic:ApiKey`. To use user secrets, run `dotnet user-secrets init` and `dotnet user-secrets set "Anthropic:ApiKey" "..."`, then pass the value explicitly as `ApiKey`. Source: https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets and https://platform.claude.com/docs/en/cli-sdks-libraries/sdks/csharp
- Secret Manager is for development only and does not encrypt values. Source: https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets

## Part B: speech input for Iraqi Arabic (stretch)

### 6. Azure Speech and ar-IQ
- The speech-to-text language table has an `ar-IQ` row (Arabic (Iraq)) with fast transcription supported. It has no real-time or batch column, and no post-stream refinement. Source: https://learn.microsoft.com/en-us/azure/ai-services/speech-service/language-support?tabs=stt
- The page does not confirm `ar-IQ` for Speech SDK real-time recognition. Check the Speech SDK locale list before committing. Source: same page (links to the Speech SDK for per-region locale lists)
- .NET SDK: NuGet `Microsoft.CognitiveServices.Speech`, version 1.48.2 per the search result (profile pages show 1.47.0 as latest, so confirm on nuget.org before pinning). Source: https://www.nuget.org/packages/Microsoft.CognitiveServices.Speech and https://packages.nuget.org/profiles/Microsoft.CognitiveServices.Speech
- Browser path: the Speech SDK for JavaScript (npm `microsoft-cognitiveservices-speech-sdk`) supports the Browser with microphone input. Source: https://learn.microsoft.com/en-us/azure/ai-services/speech-service/speech-sdk
- Blazor Server: either capture audio in the browser through JS interop and send it to the server, or run the JavaScript SDK in the page. For the second, get a short-lived authorization token from the server. The docs' JavaScript examples embed the key directly, which must not be done in browser code. Source: https://learn.microsoft.com/en-us/azure/ai-services/speech-service/how-to-recognize-speech?pivots=programming-language-javascript

### 7. Whisper and Arabic
- OpenAI's speech-to-text guide says Whisper supports 98 languages and that "accuracy varies by language". It says nothing about dialects or accents. Source: https://developers.openai.com/api/docs/guides/speech-to-text
- Arabic is in the Whisper tokenizer language table (`"ar": "arabic"`, 100 entries). Source: https://github.com/openai/whisper/blob/main/whisper/tokenizer.py
- The Whisper repository says "Whisper's performance varies widely depending on the language" and gives per-language WER only as a figure, not as text. Source: https://github.com/openai/whisper
- The Whisper paper reports multilingual training on 680,000 hours. It does not mention Arabic or dialects explicitly. Source: https://arxiv.org/abs/2212.04356
- Conclusion: the official sources say Arabic is supported but say nothing about Iraqi dialect accuracy. Any dialect claim would need the project's own measurement.

### 8. Anthropic audio input or transcription
- The Messages API guide and the models overview list text and image input (vision) with text output. Neither lists audio input or a transcription endpoint. Sources: https://platform.claude.com/docs/en/build-with-claude/working-with-messages and https://platform.claude.com/docs/en/models/overview
- A web search found no official audio-input support. The results were secondary blogs, so they were not used.
- Practical implication: transcribe with Azure Speech or Whisper first, then send the transcript text to Claude.

## Open items for Khalil
- Confirm the Anthropic `AsIChatClient` maps `ResponseFormat` and `Usage` correctly, with a test.
- Confirm `ar-IQ` real-time support in the Speech SDK locale list.
- Confirm the current Speech SDK NuGet version on nuget.org.
- Decide whether the Iraqi dialect needs a measured test, since no official source covers it.
