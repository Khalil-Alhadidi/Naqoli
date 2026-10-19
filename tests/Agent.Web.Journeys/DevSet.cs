using System.Text.Json;
using Agent.Core;
using Agent.Core.Parsing;

namespace Agent.Web.Journeys;

/// <summary>
/// The development set, read by case id: the journeys type a case's message exactly as Khalil wrote it, and the
/// console's scripted parser (the oracle over the same set) returns that case's parse.
/// </summary>
internal static class DevSet
{
    /// <summary>The text of say step number <paramref name="step"/> (0 for the first say) of case <paramref name="id"/>.</summary>
    /// <exception cref="InvalidOperationException">The set has no such case, or the case has fewer says.</exception>
    public static string Say(string id, int step = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(step);
        var path = Path.Combine(RepositoryRoot.Find(), "data", ScriptedParser.DevSetFileName);
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            using var document = JsonDocument.Parse(line);
            if (document.RootElement.GetProperty("id").GetString() != id)
                continue;
            var seen = 0;
            foreach (var element in document.RootElement.GetProperty("steps").EnumerateArray())
            {
                if (!element.TryGetProperty("say", out var say))
                    continue;
                if (seen++ == step)
                    return say.GetString() ?? throw new InvalidOperationException(id + " has a null say step.");
            }

            throw new InvalidOperationException(FormattableString.Invariant($"{id} has no say number {step}."));
        }

        throw new InvalidOperationException("The development set has no case " + id + ".");
    }

    /// <summary>
    /// The <c>expect.alias_offer</c> text of step number <paramref name="step"/> (the index in the case's <c>steps</c>,
    /// say and tap steps alike) of case <paramref name="id"/>, or null when the step names none.
    /// </summary>
    /// <exception cref="InvalidOperationException">The set has no such case or step.</exception>
    public static string? AliasOffer(string id, int step)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(step);
        var path = Path.Combine(RepositoryRoot.Find(), "data", ScriptedParser.DevSetFileName);
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            using var document = JsonDocument.Parse(line);
            if (document.RootElement.GetProperty("id").GetString() != id)
                continue;
            var steps = document.RootElement.GetProperty("steps");
            if (step >= steps.GetArrayLength())
                throw new InvalidOperationException(FormattableString.Invariant($"{id} has no step number {step}."));
            return steps[step].TryGetProperty("expect", out var expect)
                && expect.TryGetProperty("alias_offer", out var offer)
                && offer.ValueKind == JsonValueKind.String
                    ? offer.GetString()
                    : null;
        }

        throw new InvalidOperationException("The development set has no case " + id + ".");
    }

    /// <summary>
    /// The <c>not_prepared</c> text of case <paramref name="id"/>'s expected replies (milestone 9c): the names of the parts
    /// beyond the cap, in the user's own words.
    /// </summary>
    /// <exception cref="InvalidOperationException">The set has no such case, or no reply of the case names one.</exception>
    public static string NotPrepared(string id)
    {
        using var document = Case(id);
        foreach (var reply in document.RootElement.GetProperty("expect").GetProperty("replies").EnumerateArray())
        {
            if (reply.TryGetProperty("not_prepared", out var names) && names.GetString() is { } text)
                return text;
        }

        throw new InvalidOperationException(id + " names no part not prepared.");
    }

    /// <summary>The <c>expect.final.balance</c> of case <paramref name="id"/>: the wallet's balance after the case.</summary>
    public static long FinalBalance(string id)
    {
        using var document = Case(id);
        return document.RootElement.GetProperty("expect").GetProperty("final").GetProperty("balance").GetInt64();
    }

    /// <summary>The figure <paramref name="field"/> (<c>amount</c>, <c>balance_after</c>...) of expected card number <paramref name="card"/> of case <paramref name="id"/>.</summary>
    public static long CardFigure(string id, int card, string field)
    {
        using var document = Case(id);
        return document.RootElement.GetProperty("expect").GetProperty("cards")[card].GetProperty(field).GetInt64();
    }

    /// <summary>The <c>amount</c> of expected reply number <paramref name="reply"/> of case <paramref name="id"/>.</summary>
    public static long ReplyAmount(string id, int reply)
    {
        using var document = Case(id);
        return document.RootElement.GetProperty("expect").GetProperty("replies")[reply].GetProperty("amount").GetInt64();
    }

    /// <summary>The option values of expected question number <paramref name="ask"/> of case <paramref name="id"/>.</summary>
    public static IReadOnlyList<string> AskOptions(string id, int ask)
    {
        using var document = Case(id);
        return [.. document.RootElement.GetProperty("expect").GetProperty("asks")[ask].GetProperty("options").EnumerateArray().Select(o => o.GetString()!)];
    }

    /// <summary>The <c>value</c> of step number <paramref name="step"/> of case <paramref name="id"/> (a tap on an option).</summary>
    public static string TapValue(string id, int step)
    {
        using var document = Case(id);
        return document.RootElement.GetProperty("steps")[step].GetProperty("value").GetString()
            ?? throw new InvalidOperationException(FormattableString.Invariant($"{id} step {step} has no value."));
    }

    /// <summary>The line of case <paramref name="id"/>, parsed; the caller disposes it.</summary>
    private static JsonDocument Case(string id)
    {
        var path = Path.Combine(RepositoryRoot.Find(), "data", ScriptedParser.DevSetFileName);
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            var document = JsonDocument.Parse(line);
            if (document.RootElement.GetProperty("id").GetString() == id)
                return document;
            document.Dispose();
        }

        throw new InvalidOperationException("The development set has no case " + id + ".");
    }
}
