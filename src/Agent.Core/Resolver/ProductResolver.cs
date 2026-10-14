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
    /// Rule 11: when the user named no operator, the agent asks for it, even when one product fits; the options are the
    /// operators of the fitting products, in catalogue order. An operator text that names no provider is
    /// <see cref="ProductResolution.NotFound"/>, and one that names several is a question among those (Khalil, 14 Oct
    /// 2026). Rule 12 (more products than fit one question: operator first) is then the same answer. With an operator,
    /// more than
    /// <see cref="AgentOptions.MaxProductsInOneQuestion"/> fitting products cannot happen in the seed catalogue; should
    /// it, the question lists the cheapest that many, since asking for the operator again would be meaningless.
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
            var named = ProvidersNamed(typedKey, catalogue);
            switch (named.Count)
            {
                case 0:
                    return new ProductResolution.NotFound();
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
    /// The provider codes, in catalogue order, whose code, English name (lower case), Arabic name or other name has the
    /// key <paramref name="typedKey"/> (the <see cref="ArabicText.Key"/> of the typed text, lower case).
    /// </summary>
    private static List<string> ProvidersNamed(string typedKey, IReadOnlyList<WalletProduct> catalogue)
        => catalogue
            .Where(p => NamesOf(p).Any(name => ArabicText.Key(name) == typedKey))
            .Select(p => p.Provider)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IEnumerable<string> NamesOf(WalletProduct product)
    {
        yield return product.Provider.ToLowerInvariant();
        yield return product.ProviderNameEn.ToLowerInvariant();
        yield return product.ProviderNameAr;
        if (!string.IsNullOrWhiteSpace(product.AlsoCalled))
            yield return product.AlsoCalled;
    }
}
