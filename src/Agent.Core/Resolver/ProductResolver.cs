using Agent.Core.Requests;
using Agent.Core.Text;
using Agent.Core.Wallet;

namespace Agent.Core.Resolver;

/// <summary>The outcome of resolving a purchase request against the store catalogue.</summary>
public abstract record ProductResolution
{
    private ProductResolution()
    {
    }

    /// <summary>One product fits and the user named its operator: it goes to the card.</summary>
    public sealed record Card(WalletProduct Product) : ProductResolution;

    /// <summary>A <c>which_product</c> question with these products as options, ordered by price.</summary>
    public sealed record Options(IReadOnlyList<WalletProduct> Products) : ProductResolution;

    /// <summary>A <c>which_operator</c> question; the options are provider codes (<c>zain</c>, <c>asiacell</c>, <c>korek</c>).</summary>
    public sealed record AskOperator(IReadOnlyList<string> ProviderCodes) : ProductResolution;

    /// <summary>No product fits: refusal <c>product_not_found</c>.</summary>
    public sealed record NotFound : ProductResolution;
}

/// <summary>Resolves a purchase request by matching rules 11 and 12. Pure: no I/O, no wallet calls.</summary>
public static class ProductResolver
{
    /// <param name="request">The parsed request; operator text, product type and product value are read.</param>
    /// <param name="catalogue">The whole store catalogue, in wallet order.</param>
    /// <param name="options">For <see cref="AgentOptions.MaxProductsInOneQuestion"/>.</param>
    /// <remarks>
    /// Rule 11: when the user named no operator (or a text that matches no single operator), the agent asks for it, even
    /// when one product fits; the options are the operators of the fitting products, in catalogue order. Rule 12 (more
    /// products than fit one question: operator first) is then the same answer. With an operator, more than
    /// <see cref="AgentOptions.MaxProductsInOneQuestion"/> fitting products cannot happen in the seed catalogue; should
    /// it, the question lists the cheapest that many, since asking for the operator again would be meaningless.
    /// </remarks>
    public static ProductResolution Resolve(ParsedRequest request, IReadOnlyList<WalletProduct> catalogue, AgentOptions options)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(options);

        var provider = MatchOperator(request.OperatorText, catalogue);

        var fitting = catalogue
            .Where(p => provider is null || string.Equals(p.Provider, provider, StringComparison.OrdinalIgnoreCase))
            .Where(p => string.IsNullOrWhiteSpace(request.ProductType)
                || string.Equals(p.Type, request.ProductType, StringComparison.OrdinalIgnoreCase))
            .Where(p => request.ProductValue is not { } value || p.FaceValueIqd == value)
            .ToList();

        if (fitting.Count == 0)
            return new ProductResolution.NotFound();

        if (provider is null)
        {
            var providers = fitting.Select(p => p.Provider).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return new ProductResolution.AskOperator(providers);
        }

        return fitting.Count == 1
            ? new ProductResolution.Card(fitting[0])
            : new ProductResolution.Options(fitting.OrderBy(p => p.PriceIqd).Take(options.MaxProductsInOneQuestion).ToList());
    }

    /// <summary>
    /// The provider code whose code, English name (lower case), Arabic name or other name has the same
    /// <see cref="ArabicText.Key"/> as <paramref name="operatorText"/> (lower case); null when none does, or when the
    /// text names more than one provider.
    /// </summary>
    private static string? MatchOperator(string? operatorText, IReadOnlyList<WalletProduct> catalogue)
    {
        if (string.IsNullOrWhiteSpace(operatorText))
            return null;

        var typedKey = ArabicText.Key(operatorText.ToLowerInvariant());
        if (typedKey.Length == 0)
            return null;

        var matched = catalogue
            .Where(p => NamesOf(p).Any(name => ArabicText.Key(name) == typedKey))
            .Select(p => p.Provider)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return matched.Count == 1 ? matched[0] : null;
    }

    private static IEnumerable<string> NamesOf(WalletProduct product)
    {
        yield return product.Provider.ToLowerInvariant();
        yield return product.ProviderNameEn.ToLowerInvariant();
        yield return product.ProviderNameAr;
        if (!string.IsNullOrWhiteSpace(product.AlsoCalled))
            yield return product.AlsoCalled;
    }
}
