namespace Agent.Core.Parsing;

/// <summary>
/// What the parse call may know besides the user's words (D5, rule 5): the one-line note of what is pending, built by
/// code from the type and codes only, never from wallet data (no names, balances or purchase codes); null when nothing
/// is pending.
/// </summary>
public sealed record ParseContext(string? PendingNote);
