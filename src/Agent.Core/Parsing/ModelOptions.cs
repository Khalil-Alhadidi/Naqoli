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
        if (candidate.Temperature is < 0f or > 1f)
            problems.Add(prefix + "Temperature must be from 0 to 1, or unset.");
        if (candidate.InputPricePerMTok < 0 || candidate.OutputPricePerMTok < 0)
            problems.Add(prefix + "InputPricePerMTok and OutputPricePerMTok must not be below 0.");
        return problems;
    }
}

/// <summary>One model candidate: the provider, the model id, the call's limits and its prices.</summary>
public sealed class ModelCandidate
{
    public string Provider { get; set; } = ModelOptions.AnthropicProvider;

    public string ModelId { get; set; } = string.Empty;

    /// <summary>The most tokens the model may write in one parse reply.</summary>
    public int MaxOutputTokens { get; set; } = 400;

    /// <summary>
    /// The sampling temperature, or null to send none. The Claude 5.5 models reject a non-default temperature (Opus 5.5
    /// rejects any), so the shipped candidates leave it unset.
    /// </summary>
    public float? Temperature { get; set; }

    /// <summary>The longest one parse call may take before the reply is <c>model_unavailable</c>.</summary>
    public int TimeoutSeconds { get; set; } = 20;

    /// <summary>US dollars per million input tokens.</summary>
    public decimal InputPricePerMTok { get; set; }

    /// <summary>US dollars per million output tokens.</summary>
    public decimal OutputPricePerMTok { get; set; }

    /// <summary>The cost, in US dollars, of a call with these token counts.</summary>
    public decimal CostUsd(long inputTokens, long outputTokens)
        => ((inputTokens * InputPricePerMTok) + (outputTokens * OutputPricePerMTok)) / 1_000_000m;

    /// <summary>A short invariant description for logs and reports (no key).</summary>
    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"{Provider} {ModelId} (max {MaxOutputTokens} tokens, {TimeoutSeconds} s)");
}
