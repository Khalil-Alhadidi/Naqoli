using Agent.Core.Masking;

namespace Agent.Core.Parsing;

/// <summary>
/// What the parse call may know besides the user's words (D5, rule 5): the one-line note of what is pending, built by
/// code from the type and codes only, never from wallet data (no names, balances or purchase codes); null when nothing
/// is pending. <paramref name="Map"/> is the mask map of this message, for the structure validator: a token the parse
/// call returns must be one of its own; null when the caller did not mask.
/// </summary>
public sealed record ParseContext(string? PendingNote, MaskMap? Map = null);
