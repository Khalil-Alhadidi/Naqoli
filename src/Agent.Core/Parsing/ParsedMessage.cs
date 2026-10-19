using Agent.Core.Requests;

namespace Agent.Core.Parsing;

/// <summary>
/// What the parse call read in one message: every request in it, in the order spoken (D7). There is always at least one;
/// a message that cannot be read is one <see cref="RequestIntent.Unclear"/> request, so a caller that only wants the
/// first request reads <see cref="First"/>. Two messages are equal when their requests are equal in order.
/// </summary>
public sealed record ParsedMessage
{
    /// <summary>The message's requests, in the order spoken.</summary>
    /// <exception cref="ArgumentException">The list is empty or holds a null entry.</exception>
    public ParsedMessage(IReadOnlyList<ParsedRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count == 0)
            throw new ArgumentException("A parsed message holds at least one request.", nameof(requests));
        if (requests.Any(r => r is null))
            throw new ArgumentException("A parsed message holds no null request.", nameof(requests));

        Requests = [.. requests];
    }

    /// <summary>Every request of the message, in the order spoken; never empty.</summary>
    public IReadOnlyList<ParsedRequest> Requests { get; }

    /// <summary>The first request spoken.</summary>
    public ParsedRequest First => Requests[0];

    /// <summary>A message of one request.</summary>
    public static ParsedMessage One(ParsedRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new ParsedMessage([request]);
    }

    public bool Equals(ParsedMessage? other)
        => other is not null && (ReferenceEquals(this, other) || Requests.SequenceEqual(other.Requests));

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var request in Requests)
            hash.Add(request);
        return hash.ToHashCode();
    }
}
