using Agent.Core.Masking;
using Agent.Core.Trace;

namespace Agent.Core.Parsing;

/// <summary>
/// What the parse call may know besides the user's words (D5, rule 5): the one-line note of what is pending, built by
/// code from the type and codes only, never from wallet data (no names, balances or purchase codes); null when nothing
/// is pending. <paramref name="Map"/> is the mask map of this message, for the structure validator: a token the parse
/// call returns must be one of its own; null when the caller did not mask. <paramref name="DinarWords"/> is
/// <see cref="AgentOptions.DinarWords"/> for <see cref="DinarUnitGuard"/>, applied by code after the validator and never
/// sent to the model; null or empty turns the guard off. <paramref name="Trace"/> is the caller's trace sink, where a
/// parser without one of its own (the singleton <see cref="ScriptedParser"/>) writes the guard steps
/// (<see cref="ParseGuards.Record"/>); null writes none.
/// </summary>
public sealed record ParseContext(
    string? PendingNote, MaskMap? Map = null, IReadOnlyCollection<string>? DinarWords = null, ITraceSink? Trace = null);
