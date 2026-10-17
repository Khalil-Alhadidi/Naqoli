using Agent.Core.Text;

namespace Agent.Core.Resolver;

/// <summary>
/// Resolves a typed name against a user's saved names and aliases by matching rules 1 to 8 and 15
/// (<c>docs/matching-rules.md</c>). Pure: no I/O, no wallet calls. Steps, first that hits wins: exact, near, partial,
/// shared word, not found (near before partial: Khalil, 15 Oct 2026). When nothing matches exactly and the typed name
/// holds an endearment, the endearments are dropped and the steps run again from exact (rule 1, 17 Oct 2026).
/// </summary>
public static class ContactResolver
{
    /// <summary>
    /// The default of <see cref="AgentOptions.MinSubstringWordLength"/>: a typed word whose key (normalised, leading al
    /// stripped) is shorter than this must equal a whole word of the candidate in the partial step; longer words may
    /// occur anywhere inside the candidate's key. Rule 4 as Khalil decided it on 14 Oct 2026 (one- and two-letter words
    /// are whole words only).
    /// </summary>
    public const int MinSubstringWordLength = 3;

    /// <summary>Resolves with the default minimum, <see cref="MinSubstringWordLength"/>.</summary>
    public static Resolution Resolve(string typedName, IReadOnlyList<ContactCandidate> candidates)
        => Resolve(typedName, candidates, MinSubstringWordLength);

    /// <param name="typedName">The name as the user typed it.</param>
    /// <param name="candidates">The user's saved names and aliases (<see cref="ContactCandidate.From"/>).</param>
    /// <param name="minSubstringWordLength">
    /// <see cref="AgentOptions.MinSubstringWordLength"/>: a shorter typed word must equal a whole word of a candidate.
    /// </param>
    public static Resolution Resolve(string typedName, IReadOnlyList<ContactCandidate> candidates, int minSubstringWordLength)
        => Resolve(typedName, candidates, minSubstringWordLength, []);

    /// <param name="typedName">The name as the user typed it.</param>
    /// <param name="candidates">The user's saved names and aliases (<see cref="ContactCandidate.From"/>).</param>
    /// <param name="minSubstringWordLength">
    /// <see cref="AgentOptions.MinSubstringWordLength"/>: a shorter typed word must equal a whole word of a candidate.
    /// </param>
    /// <param name="endearments">
    /// <see cref="AgentOptions.Endearments"/> (rule 1, Khalil, 17 Oct 2026): words of affection that are dropped from the
    /// typed name, and the whole matching run again on what is left, only when nothing matches the full text exactly, so
    /// an alias that itself contains one of these words wins untouched. Compared whole word after the letter
    /// normalisation, with no al strip (the words carry their article). Nothing left after the drop is not found.
    /// </param>
    public static Resolution Resolve(
        string typedName, IReadOnlyList<ContactCandidate> candidates, int minSubstringWordLength, IReadOnlyCollection<string> endearments)
    {
        ArgumentNullException.ThrowIfNull(typedName);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(endearments);
        ArgumentOutOfRangeException.ThrowIfLessThan(minSubstringWordLength, 1);

        var typedKey = ArabicText.Key(typedName);
        if (typedKey.Length == 0)
            return new Resolution.NotFound();

        var keyed = candidates.Select(c => (Candidate: c, Key: ArabicText.Key(c.Text))).ToList();

        // 1. Exact (rules 1, 2, 3, 7): the whole key equals a saved name or alias; hits on one account count once.
        var exact = keyed.Where(k => k.Key == typedKey).Select(k => k.Candidate).ToList();
        if (exact.Count > 0)
        {
            var accounts = OnePerAccount(exact);
            return accounts.Count == 1
                ? new Resolution.Card(accounts[0])
                : new Resolution.Options(QuestionKind.WhichContact, accounts);
        }

        // Rule 1, endearments (Khalil, 17 Oct 2026): nothing matched the full text exactly, so the words of affection
        // are dropped and the whole matching runs again, from the exact step, on what is left.
        if (WithoutEndearments(typedName, endearments) is { } rest)
        {
            return rest.Length == 0
                ? new Resolution.NotFound()
                : Resolve(rest, candidates, minSubstringWordLength, []);
        }

        // A typed name of title and kinship words only is not a name (rules 1 and 15).
        var typedWords = ArabicText.Words(typedName);
        if (typedWords.All(w => ArabicText.IsTitle(w) || ArabicText.IsKinship(w)))
            return new Resolution.NotFound();

        // 2. Near (rule 6): the whole key is one edit away from a whole saved name or alias; always a question, even
        // with one candidate. Checked before partial (Khalil, 15 Oct 2026, CLN-03-v1): a name one letter short of a saved
        // one is also inside it, and the user is asked rather than sent straight to the card.
        var near = keyed.Where(k => ArabicText.EditDistance(k.Key, typedKey) == 1).Select(k => k.Candidate).ToList();
        if (near.Count > 0)
            return new Resolution.Options(QuestionKind.DidYouMean, OnePerAccount(near));

        // 3. Partial (rules 4, 5, 7): every typed word is inside the candidate's key; a short typed word must equal
        // one of the candidate's words (both compared after the per-word leading al strip). A word's length is measured
        // on its key, after that strip.
        var wordKeys = typedWords
            .Select(ArabicText.Key)
            .Where(key => key.Length > 0)
            .Select(key => (Key: key, WholeWordOnly: key.Length < minSubstringWordLength))
            .ToList();
        var typedHasTitle = typedWords.Any(ArabicText.IsTitle);
        var partial = keyed
            .Where(k => wordKeys.All(w => w.WholeWordOnly
                ? ArabicText.Words(k.Candidate.Text).Any(cw => ArabicText.StripLeadingAl(cw) == w.Key)
                : k.Key.Contains(w.Key, StringComparison.Ordinal)))
            .Select(k => k.Candidate)
            .ToList();
        if (partial.Count > 0)
        {
            var accounts = OnePerAccount(partial);

            // Rule 5: a "title + name" contact matched by the bare name is only ever an option. An account is
            // title-only when every hit on it is.
            var titleOnly = !typedHasTitle && partial
                .Where(c => c.AccountId == accounts[0].AccountId)
                .All(IsTitleName);

            return accounts.Count == 1 && !titleOnly
                ? new Resolution.Card(accounts[0])
                : new Resolution.Options(QuestionKind.WhichContact, accounts);
        }

        // 4. Shared word (rule 15): a name word (not a title, not a kinship word) shared exactly; never a card.
        var nameWords = typedWords
            .Where(w => !ArabicText.IsTitle(w) && !ArabicText.IsKinship(w))
            .Select(ArabicText.StripLeadingAl)
            .ToHashSet(StringComparer.Ordinal);
        var shared = candidates
            .Where(c => ArabicText.Words(c.Text).Any(w => nameWords.Contains(ArabicText.StripLeadingAl(w))))
            .ToList();
        if (shared.Count > 0)
            return new Resolution.Options(QuestionKind.WhichContact, OnePerAccount(shared));

        // 5. None (rule 8).
        return new Resolution.NotFound();
    }

    /// <summary>
    /// The typed name without its endearments, words joined by one space (empty when nothing is left), or null when no
    /// typed word is one.
    /// </summary>
    private static string? WithoutEndearments(string typedName, IReadOnlyCollection<string> endearments)
    {
        if (endearments.Count == 0)
            return null;

        var words = endearments
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(ArabicText.NormalizeLetters)
            .ToHashSet(StringComparer.Ordinal);
        var typedWords = ArabicText.NormalizeLetters(typedName).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return typedWords.Any(words.Contains)
            ? string.Join(' ', typedWords.Where(w => !words.Contains(w)))
            : null;
    }

    private static bool IsTitleName(ContactCandidate candidate)
    {
        var words = ArabicText.Words(candidate.Text);
        return words.Count > 1 && ArabicText.IsTitle(words[0]);
    }

    /// <summary>
    /// One candidate per account (rule 7), ordered by the account's first hit; the alias is shown when the account was
    /// hit by both an alias and a saved name.
    /// </summary>
    private static List<ContactCandidate> OnePerAccount(List<ContactCandidate> hits)
        => hits
            .GroupBy(c => c.AccountId)
            .Select(g => g.FirstOrDefault(c => c.IsAlias) ?? g.First())
            .ToList();
}
