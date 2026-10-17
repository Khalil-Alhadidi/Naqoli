namespace Agent.Core.Resolver;

/// <summary>The question a set of name options asks (question kinds <c>which_contact</c> and <c>did_you_mean</c>).</summary>
public enum QuestionKind
{
    /// <summary><c>which_contact</c>: several exact or partial hits, a title-only hit, or a shared name word.</summary>
    WhichContact,

    /// <summary><c>did_you_mean</c>: a near spelling (rule 6), even with one candidate.</summary>
    DidYouMean,
}

/// <summary>The outcome of resolving a typed name against the user's contacts and aliases.</summary>
public abstract record Resolution
{
    private Resolution()
    {
    }

    /// <summary>One account: the name goes straight to the card.</summary>
    /// <param name="Chosen">The matched candidate; an alias when the account was matched by both.</param>
    public sealed record Card(ContactCandidate Chosen) : Resolution;

    /// <summary>A question. One candidate per account, in candidate order, each with the text that matched.</summary>
    public sealed record Options(QuestionKind Kind, IReadOnlyList<ContactCandidate> Candidates) : Resolution;

    /// <summary>No hit (rule 8): no card.</summary>
    public sealed record NotFound : Resolution;
}

/// <summary>
/// A typed answer to a contact question, matched by <see cref="ContactResolver.ResolveAnswer"/>: the form of the answer
/// that matched (as typed, or with the preposition lam dropped) and its resolution.
/// </summary>
/// <param name="Text">The form that matched; the conversation puts it in the request's recipient.</param>
/// <param name="Resolution">What the matching gave for <paramref name="Text"/>.</param>
public sealed record AnswerMatch(string Text, Resolution Resolution);
