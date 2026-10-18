using System.Globalization;

namespace Agent.Core.Parsing;

/// <summary>
/// The model candidates and the one in use, bound from the <c>Model</c> section. A candidate is a configuration change
/// (CLAUDE.md, Stack). Validated at start only when <c>Agent:Parser</c> is <c>claude</c> (<see cref="ParserFactory"/>).
/// The key is not here: it is read from <c>Anthropic:ApiKey</c> or <c>ANTHROPIC_API_KEY</c> and never shown.
/// </summary>
public sealed class ModelOptions
{
    public const string SectionName = "Model";

    /// <summary>The only provider built: Anthropic, through <c>Microsoft.Extensions.AI</c>.</summary>
    public const string AnthropicProvider = "anthropic";

    /// <summary>The effort levels the API accepts in <c>output_config.effort</c>.</summary>
    public static IReadOnlyList<string> EffortLevels { get; } = ["low", "medium", "high", "xhigh", "max"];

    /// <summary>The prompt cache lifetime that marks the system prompt cacheable for five minutes (the default).</summary>
    public const string CacheTtl5m = "5m";

    /// <summary>The prompt cache lifetime that marks the system prompt cacheable for one hour.</summary>
    public const string CacheTtl1h = "1h";

    /// <summary>The prompt cache setting that sends no cache control at all.</summary>
    public const string CacheTtlNone = "none";

    /// <summary>The values <see cref="ModelCandidate.PromptCacheTtl"/> accepts.</summary>
    public static IReadOnlyList<string> PromptCacheTtls { get; } = [CacheTtl5m, CacheTtl1h, CacheTtlNone];

    /// <summary>The name of the candidate in use (a key of <see cref="Candidates"/>).</summary>
    public string? Candidate { get; set; }

    /// <summary>The candidates by name (<c>haiku</c>, <c>sonnet</c>, <c>opus</c>).</summary>
    public Dictionary<string, ModelCandidate> Candidates { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The candidate in use, or null when <see cref="Candidate"/> names none.</summary>
    public ModelCandidate? Selected
        => Candidate is not null && Candidates.TryGetValue(Candidate, out var candidate) ? candidate : null;

    /// <summary>What is wrong with the candidate in use, one sentence each; empty when it can be used.</summary>
    public IReadOnlyList<string> Problems()
    {
        if (Selected is not { } candidate)
            return ["Model:Candidate must name one of Model:Candidates (" + string.Join(", ", Candidates.Keys) + ")."];

        var prefix = "Model:Candidates:" + Candidate + ":";
        var problems = new List<string>();
        if (!string.Equals(candidate.Provider, AnthropicProvider, StringComparison.Ordinal))
            problems.Add(prefix + "Provider must be '" + AnthropicProvider + "'.");
        if (string.IsNullOrWhiteSpace(candidate.ModelId))
            problems.Add(prefix + "ModelId is required.");
        if (candidate.MaxOutputTokens <= 0)
            problems.Add(prefix + "MaxOutputTokens must be above 0.");
        if (candidate.TimeoutSeconds <= 0)
            problems.Add(prefix + "TimeoutSeconds must be above 0.");
        if (!EffortLevels.Contains(candidate.Effort, StringComparer.Ordinal))
            problems.Add(prefix + "Effort must be one of " + string.Join(", ", EffortLevels) + ".");
        if (candidate.Temperature is < 0f or > 1f)
            problems.Add(prefix + "Temperature must be from 0 to 1, or unset.");
        if (candidate.InputPricePerMTok < 0 || candidate.OutputPricePerMTok < 0
            || candidate.CacheWritePricePerMTok < 0 || candidate.CacheReadPricePerMTok < 0)
            problems.Add(prefix + "InputPricePerMTok, OutputPricePerMTok, CacheWritePricePerMTok and CacheReadPricePerMTok must not be below 0.");
        if (!PromptCacheTtls.Contains(candidate.PromptCacheTtl, StringComparer.Ordinal))
            problems.Add(prefix + "PromptCacheTtl must be one of " + string.Join(", ", PromptCacheTtls) + ".");
        return problems;
    }
}

/// <summary>One model candidate: the provider, the model id, the call's limits and its prices.</summary>
public sealed class ModelCandidate
{
    public string Provider { get; set; } = ModelOptions.AnthropicProvider;

    public string ModelId { get; set; } = string.Empty;

    /// <summary>
    /// The most tokens the model may write in one parse reply. The 5.5 models think by default and thinking counts against
    /// this limit, so it leaves room for both; a reply cut off at it is <c>Problem</c> <c>truncated</c>.
    /// </summary>
    public int MaxOutputTokens { get; set; } = 1500;

    /// <summary>
    /// How much the model thinks (<c>output_config.effort</c>): <c>low</c> (the default), <c>medium</c>, <c>high</c>,
    /// <c>xhigh</c> or <c>max</c>. Parsing one message needs little; the models' own default is <c>medium</c> or <c>high</c>.
    /// </summary>
    public string Effort { get; set; } = "low";

    /// <summary>
    /// The sampling temperature, or null to send none (the default). Keep it unset for the Claude 5.5 models: Haiku 5.5 and
    /// Sonnet 5.5 answer a non-default temperature with HTTP 400, and Opus 5.5 answers any temperature with 400.
    /// </summary>
    public float? Temperature { get; set; }

    /// <summary>The longest one parse call may take before the reply is <c>model_unavailable</c>.</summary>
    public int TimeoutSeconds { get; set; } = 20;

    /// <summary>US dollars per million input tokens.</summary>
    public decimal InputPricePerMTok { get; set; }

    /// <summary>US dollars per million output tokens.</summary>
    public decimal OutputPricePerMTok { get; set; }

    /// <summary>
    /// US dollars per million input tokens written to the prompt cache (the API's <c>cache_creation_input_tokens</c>);
    /// for a five-minute write, 1.25 times <see cref="InputPricePerMTok"/> on the price list of 18 Oct 2026.
    /// </summary>
    public decimal CacheWritePricePerMTok { get; set; }

    /// <summary>
    /// US dollars per million input tokens read from the prompt cache (the API's <c>cache_read_input_tokens</c>).
    /// </summary>
    public decimal CacheReadPricePerMTok { get; set; }

    /// <summary>
    /// How long the cached system prompt lives: <c>5m</c> (the default), <c>1h</c>, or <c>none</c> to send no cache
    /// control. Only the frozen prompt is ever marked cacheable, never the user's message.
    /// </summary>
    public string PromptCacheTtl { get; set; } = ModelOptions.CacheTtl5m;

    /// <summary>
    /// The cost, in US dollars, of a call with these token counts. <paramref name="inputTokens"/> is the API's
    /// <c>input_tokens</c>, the uncached part only; the cache writes and reads are counted and priced apart.
    /// </summary>
    public decimal CostUsd(long inputTokens, long outputTokens, long cacheWriteTokens, long cacheReadTokens)
        => ((inputTokens * InputPricePerMTok)
            + (outputTokens * OutputPricePerMTok)
            + (cacheWriteTokens * CacheWritePricePerMTok)
            + (cacheReadTokens * CacheReadPricePerMTok)) / 1_000_000m;

    /// <summary>A short invariant description for logs and reports (no key).</summary>
    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"{Provider} {ModelId} (effort {Effort}, max {MaxOutputTokens} tokens, {TimeoutSeconds} s)");
}
