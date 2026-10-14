namespace Agent.Core.Parsing;

/// <summary>What one parse call took: tokens in and out, the cost in US dollars, and the time.</summary>
public sealed record ParseUsage(int InputTokens, int OutputTokens, decimal CostUsd, TimeSpan Duration);
