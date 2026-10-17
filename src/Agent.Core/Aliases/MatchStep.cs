namespace Agent.Core.Aliases;

/// <summary>
/// The matching step of <see cref="Resolver.ContactResolver"/> that produced a question (<c>docs/matching-rules.md</c>).
/// Rule 18 reads it: an alias is offered after a question from <see cref="Near"/>, <see cref="SharedWord"/> or
/// <see cref="TitleOnly"/>, never after one from <see cref="Exact"/> or <see cref="Partial"/> (a bare name shared by
/// several; milestone 9a, ruling 2).
/// </summary>
public enum MatchStep
{
    /// <summary>Rule 3: the typed name equals a saved name or alias of several accounts.</summary>
    Exact,

    /// <summary>Rule 6: the typed name is one edit from a saved name or alias (<c>did_you_mean</c>).</summary>
    Near,

    /// <summary>Rule 4: the typed words are inside the saved names or aliases of several accounts.</summary>
    Partial,

    /// <summary>Rule 5: one account, a "title + name" contact matched by the bare name; always one candidate.</summary>
    TitleOnly,

    /// <summary>Rule 15: a name word shared exactly with one or more saved names or aliases.</summary>
    SharedWord,
}
