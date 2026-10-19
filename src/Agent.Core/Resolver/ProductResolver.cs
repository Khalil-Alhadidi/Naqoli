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
    /// <param name="ProviderCode">
    /// The provider the typed operator named; null when no operator was named or the typed text named no provider.
    /// </param>
    public sealed record NotFound(string? ProviderCode) : ProductResolution;
}

/// <summary>Resolves a purchase request by matching rules 11 and 12. Pure: no I/O, no wallet calls.</summary>
public static class ProductResolver
{
    /// <param name="request">The parsed request; operator text, product type and product value are read.</param>
    /// <param name="catalogue">The whole store catalogue, in wallet order.</param>
    /// <param name="options">
    /// For <see cref="AgentOptions.MaxProductsInOneQuestion"/> and the operator spellings
    /// (<see cref="AgentOptions.ProviderSpellings"/>, by provider code).
    /// </param>
    /// <remarks>
    /// Rules 11 and 12 as Khalil decided them (19 Oct 2026). With no operator named, the products that fit the type and
    /// the value are listed as one <c>which_product</c> question across the operators when they number
    /// <see cref="AgentOptions.MaxProductsInOneQuestion"/> or fewer, even when only one fits (never a card: the user
    /// has not chosen an operator); above that, the operator is asked first, among the providers of the fitting products
    /// in catalogue order. Options are ordered by price, catalogue order within a price. An operator text that names no
    /// provider is <see cref="ProductResolution.NotFound"/>, and one that names several is a question among those
    /// (Khalil, 14 Oct 2026). With an operator, one fit is the card and several are options; more than
    /// <see cref="AgentOptions.MaxProductsInOneQuestion"/> cannot happen in the seed catalogue; should it, the question
    /// lists the cheapest that many, since asking for the operator again would be meaningless.
    /// </remarks>
    public static ProductResolution Resolve(ParsedRequest request, IReadOnlyList<WalletProduct> catalogue, AgentOptions options)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(options);

        string? provider = null;
        var typedKey = request.OperatorText is null ? string.Empty : ArabicText.Key(request.OperatorText.ToLowerInvariant());
        if (typedKey.Length > 0)
        {
            // Khalil, 14 Oct 2026: a named operator that is no provider is refused, so the learning log sees it.
            var named = ProvidersNamed(typedKey, catalogue, options);
            switch (named.Count)
            {
                case 0:
                    return new ProductResolution.NotFound(null);
                case 1:
                    provider = named[0];
                    break;
                default:
                    return new ProductResolution.AskOperator(named);
            }
        }

        var fitting = catalogue
            .Where(p => provider is null || string.Equals(p.Provider, provider, StringComparison.OrdinalIgnoreCase))
            .Where(p => string.IsNullOrWhiteSpace(request.ProductType)
                || string.Equals(p.Type, request.ProductType, StringComparison.OrdinalIgnoreCase))
            .Where(p => request.ProductValue is not { } value || p.FaceValueIqd == value)
            .ToList();

        if (fitting.Count == 0)
            return new ProductResolution.NotFound(provider);

        if (provider is null)
        {
            if (fitting.Count <= options.MaxProductsInOneQuestion)
                return new ProductResolution.Options(ByPrice(fitting).ToList());

            var providers = fitting.Select(p => p.Provider).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return new ProductResolution.AskOperator(providers);
        }

        return fitting.Count == 1
            ? new ProductResolution.Card(fitting[0])
            : new ProductResolution.Options(ByPrice(fitting).Take(options.MaxProductsInOneQuestion).ToList());
    }

    /// <summary>By price; the sort is stable, so catalogue order within a price.</summary>
    private static IOrderedEnumerable<WalletProduct> ByPrice(IEnumerable<WalletProduct> products)
        => products.OrderBy(p => p.PriceIqd);

    /// <summary>
    /// The provider codes, in catalogue order, whose code, English name (lower case), Arabic name, other name or
    /// configured spelling (<see cref="AgentOptions.ProviderSpellings"/> under the provider code, MAL-09) has the key
    /// <paramref name="typedKey"/> (the <see cref="ArabicText.Key"/> of the typed text, lower case).
    /// </summary>
    private static List<string> ProvidersNamed(string typedKey, IReadOnlyList<WalletProduct> catalogue, AgentOptions options)
        => catalogue
            .Where(p => NamesOf(p, options).Any(name => ArabicText.Key(name) == typedKey))
            .Select(p => p.Provider)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IEnumerable<string> NamesOf(WalletProduct product, AgentOptions options)
    {
        yield return product.Provider.ToLowerInvariant();
        yield return product.ProviderNameEn.ToLowerInvariant();
        yield return product.ProviderNameAr;
        if (!string.IsNullOrWhiteSpace(product.AlsoCalled))
            yield return product.AlsoCalled;
        if (options.ProviderSpellings.TryGetValue(product.Provider, out var spellings))
        {
            foreach (var spelling in spellings)
                yield return spelling.ToLowerInvariant();
        }
    }
}
