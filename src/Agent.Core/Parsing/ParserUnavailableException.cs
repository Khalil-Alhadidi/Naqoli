namespace Agent.Core.Parsing;

/// <summary>
/// Thrown by a parser when the parse call could not be made or gave no answer (the model is down, times out, or is
/// switched off by a fault). The conversation answers it with <c>model_unavailable</c>: no card, nothing taken.
/// </summary>
public sealed class ParserUnavailableException : Exception
{
    public ParserUnavailableException()
        : base("The parser is unavailable.")
    {
    }

    public ParserUnavailableException(string message)
        : base(message)
    {
    }

    public ParserUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
