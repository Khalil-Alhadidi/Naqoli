namespace Agent.Core.Parsing;

/// <summary>
/// What one parse call took: the tokens, the cost in US dollars, and the time. <see cref="InputTokens"/> is the API's
/// <c>input_tokens</c>, the uncached part only; <see cref="CacheWriteTokens"/> (<c>cache_creation_input_tokens</c>) and
/// <see cref="CacheReadTokens"/> (<c>cache_read_input_tokens</c>) are the prompt cache's, so the call's whole input is
/// the sum of the three. <see cref="CostUsd"/> prices all four counts.
/// </summary>
public sealed record ParseUsage(
    int InputTokens, int OutputTokens, int CacheWriteTokens, int CacheReadTokens, decimal CostUsd, TimeSpan Duration);
