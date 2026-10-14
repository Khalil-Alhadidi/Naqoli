using Agent.Core.Parsing;
using Agent.Core.Requests;

namespace Agent.Eval.Running;

/// <summary>A parser fault the runner injects on the model call (<c>fault.on == "request"</c>).</summary>
public enum ParserFault
{
    /// <summary><c>model_unreachable</c>: the call throws <see cref="ParserUnavailableException"/>, so the reply is <c>model_unavailable</c>.</summary>
    Unreachable,

    /// <summary>
    /// <c>model_invalid_reply</c>: the call returns an <see cref="RequestIntent.Unclear"/> request (until milestone 6
    /// validates the model's structure, an invalid reply is a request the agent did not understand).
    /// </summary>
    InvalidReply,
}

/// <summary>
/// The runner's parser: it records every text handed to the parse call (for the outbound-text check, D12), which is
/// the masked text, since the conversation masks every number before it calls the parser (rule 5, review R3), and each
/// call's usage, applies an armed <see cref="ParserFault"/>, and otherwise passes the call to the inner parser.
/// </summary>
public sealed class RecordingParser(IParser inner) : IParser, IParserUsage
{
    private readonly Lock gate = new();
    private readonly List<string> texts = [];
    private readonly List<ParseUsage> usages = [];
    private ParserFault? fault;
    private int faultTimes;
    private ParseUsage? lastUsage;

    /// <summary>Every text handed to the parse call since the last <see cref="Reset"/>, in order.</summary>
    public IReadOnlyList<string> Texts
    {
        get
        {
            lock (gate)
                return [.. texts];
        }
    }

    /// <summary>The usage of every completed parse call since the last <see cref="Reset"/>.</summary>
    public IReadOnlyList<ParseUsage> Usages
    {
        get
        {
            lock (gate)
                return [.. usages];
        }
    }

    public ParseUsage? LastUsage
    {
        get
        {
            lock (gate)
                return lastUsage;
        }
    }

    /// <summary>Makes the next <paramref name="times"/> parse calls fail with <paramref name="parserFault"/>.</summary>
    public void Arm(ParserFault parserFault, int times)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(times);
        lock (gate)
        {
            fault = parserFault;
            faultTimes = times;
        }
    }

    /// <summary>Forgets the texts, the usages and any armed fault.</summary>
    public void Reset()
    {
        lock (gate)
        {
            texts.Clear();
            usages.Clear();
            fault = null;
            faultTimes = 0;
            lastUsage = null;
        }
    }

    public async Task<ParsedRequest> ParseAsync(string text, ParseContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(context);

        ParserFault? injected = null;
        lock (gate)
        {
            texts.Add(text);
            if (fault is { } armed && faultTimes > 0)
            {
                injected = armed;
                if (--faultTimes == 0)
                    fault = null;
            }
        }

        switch (injected)
        {
            case ParserFault.Unreachable:
                throw new ParserUnavailableException("Injected fault: model_unreachable.");
            case ParserFault.InvalidReply:
                return new ParsedRequest { Intent = RequestIntent.Unclear };
        }

        var parsed = await inner.ParseAsync(text, context, cancellationToken);
        if (inner is IParserUsage { LastUsage: { } usage })
        {
            lock (gate)
            {
                usages.Add(usage);
                lastUsage = usage;
            }
        }

        return parsed;
    }
}
