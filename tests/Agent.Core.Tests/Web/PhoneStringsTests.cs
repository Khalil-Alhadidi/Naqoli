extern alias AgentWeb;

using System.Globalization;
using AgentWeb::Agent.Web.Services;

namespace Agent.Core.Tests.Web;

/// <summary>The phone's resource file (<c>src/Agent.Web/Resources/PhoneStrings.resx</c>): Khalil's wording, complete and well formed.</summary>
public class PhoneStringsTests
{
    private const string Placeholder = "TODO-AR";

    private static Dictionary<string, string> Values() => PhoneStringsFile.Values();

    /// <summary>
    /// Keys added after Khalil wrote the phone's wording and still English placeholders until he writes them (empty from
    /// 17 Oct 2026; the eight keys of milestone 9b had his wording on 19 Oct 2026, so empty again; milestone 9c adds the
    /// three of the "not prepared" line and its close-out <c>Reply_queue_dropped</c>, which had his wording on 20 Oct
    /// 2026, so empty again); a new key goes here until its Arabic is in, and no other key may be a placeholder. In
    /// ordinal order.
    /// </summary>
    private static readonly string[] AwaitingKhalil = [];

    /// <summary>
    /// Keys whose Arabic Claude drafted at Khalil's request and he has not yet reviewed (the Help screen, 22 Oct 2026:
    /// "write the translation your self and I will modify it after"). They are real wording, not placeholders, so they
    /// are not in <see cref="AwaitingKhalil"/>; this list is emptied when he has read them. In ordinal order.
    /// </summary>
    private static readonly string[] DraftedByClaudePendingReview =
    [
        "Help_Balance", "Help_Bill", "Help_Intro", "Help_NotDone", "Help_Open", "Help_Purchase", "Help_Title", "Help_Transfer",
    ];

    [Fact]
    public void NoValueIsAPlaceholderExceptTheKeysAwaitingKhalil()
    {
        var placeholders = Values()
            .Where(v => v.Value.StartsWith(Placeholder, StringComparison.Ordinal))
            .Select(v => v.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(AwaitingKhalil, placeholders);
    }

    [Fact]
    public void EveryKeyDraftedByClaudeExistsAndIsNoPlaceholder()
    {
        var values = Values();
        Assert.Equal(DraftedByClaudePendingReview.Order(StringComparer.Ordinal), DraftedByClaudePendingReview);
        Assert.Empty(DraftedByClaudePendingReview.Intersect(AwaitingKhalil, StringComparer.Ordinal));
        foreach (var key in DraftedByClaudePendingReview)
        {
            Assert.True(values.ContainsKey(key), key + " is missing");
            Assert.False(values[key].StartsWith(Placeholder, StringComparison.Ordinal), key + " is a placeholder");
        }
    }

    [Fact]
    public void EveryProductTypeOfTheSeedHasItsTypeWord()
    {
        // Security review M3 of milestone 9b: the type word's key is built from the wallet's product type.
        var keys = Values();
        var types = global::Agent.Core.Tests.Wallet.SeedFacts.Seed.Products.Select(p => p.Type).Distinct(StringComparer.Ordinal).ToList();

        Assert.NotEmpty(types);
        Assert.All(types, type => Assert.True(keys.ContainsKey("Product_type_" + type), "Product_type_" + type + " is missing"));
    }

    [Fact]
    public void NoValueIsBlankOrHasAnEdgeSpace()
    {
        foreach (var (key, value) in Values())
        {
            Assert.False(string.IsNullOrWhiteSpace(value), key + " is blank");
            Assert.Equal(value.Trim(), value);
        }
    }

    [Fact]
    public void EveryValueFormatsWithTwoArguments()
    {
        // A stray brace would throw at the moment a reply is built; braces here are {0} and {1} only.
        foreach (var (key, value) in Values())
        {
            var formatted = string.Format(CultureInfo.InvariantCulture, value, "a", "b");
            Assert.DoesNotContain('{', formatted);
            Assert.DoesNotContain('}', formatted);
        }
    }

    /// <summary>
    /// Security review M3 of milestone 9c: every value that holds a placeholder formats with as many arguments as its
    /// highest index plus one, so a slip in a template (a stray brace, a skipped index) fails here, not on the phone.
    /// </summary>
    [Fact]
    public void EveryTemplateFormatsWithItsOwnPlaceholders()
    {
        foreach (var (key, value) in Values().Where(v => v.Value.Contains('{', StringComparison.Ordinal)))
        {
            var indexes = System.Text.RegularExpressions.Regex.Matches(value, @"\{(\d+)\}")
                .Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
                .ToList();
            Assert.True(indexes.Count > 0, key + " holds a brace that is no placeholder");
            var arguments = Enumerable.Range(0, indexes.Max() + 1).Select(i => (object)("arg" + i.ToString(CultureInfo.InvariantCulture))).ToArray();

            var formatted = string.Format(CultureInfo.InvariantCulture, value, arguments);

            Assert.DoesNotContain('{', formatted);
            Assert.DoesNotContain('}', formatted);
        }
    }

    [Fact]
    public void TheYesAndNoListsSplitIntoWordsThatDoNotOverlap()
    {
        var values = Values();
        var yes = PhoneWords.Split(values["Word_yes"]);
        var no = PhoneWords.Split(values["Word_no"]);
        Assert.NotEmpty(yes);
        Assert.NotEmpty(no);
        Assert.All(yes.Concat(no), w => Assert.Equal(w.Trim(), w));
        Assert.Empty(yes.Intersect(no, StringComparer.OrdinalIgnoreCase));
    }
}
