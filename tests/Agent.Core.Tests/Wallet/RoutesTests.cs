using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Agent.Core.Tests.Wallet;

/// <summary>
/// Rule 11 (milestone 9b close-out, 19 Oct 2026): a code cannot be returned. The mock wallet maps no route that returns,
/// refunds, reverses or cancels an order or a transaction, and its only writes are the documented ones of
/// <c>docs/wallet-api.md</c>.
/// </summary>
[Collection(WalletApiCollection.Name)]
public sealed partial class RoutesTests(WalletApiFixture fixture)
{
    private static readonly string[] ReversingWords = ["refund", "return", "reverse", "cancel"];

    private static readonly string[] ChangingMethods = ["DELETE", "PUT", "PATCH"];

    [Fact]
    public async Task TheWalletExposesNoRouteThatReversesOrRefundsAnOrder()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);

        await using var scope = fixture.CreateScope();
        var routes = scope.ServiceProvider.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(e => (e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [])
                .Select(method => (Method: method, Pattern: Normalise(e.RoutePattern.RawText ?? string.Empty))))
            .ToList();

        Assert.NotEmpty(routes);
        Assert.All(routes, r => Assert.DoesNotContain(
            ReversingWords, word => r.Pattern.Contains(word, StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(routes, r =>
            (r.Pattern.StartsWith("/users/{id}/orders", StringComparison.Ordinal) || r.Pattern.StartsWith("/transactions", StringComparison.Ordinal))
            && ChangingMethods.Contains(r.Method, StringComparer.OrdinalIgnoreCase));

        var writes = routes
            .Where(r => !HttpMethods.IsGet(r.Method) && !HttpMethods.IsHead(r.Method))
            .Select(r => r.Method.ToUpperInvariant() + " " + r.Pattern)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(DocumentedWrites(), writes);
    }

    /// <summary>The non-GET routes of the "Endpoints" table of <c>docs/wallet-api.md</c>, as <c>METHOD pattern</c>, query strings dropped.</summary>
    private static string[] DocumentedWrites()
    {
        var path = Path.Combine(SeedHashTests.RepositoryRoot(), "docs", "wallet-api.md");
        var writes = File.ReadLines(path)
            .Select(line => TableRoute().Match(line))
            .Where(m => m.Success && m.Groups["method"].Value != "GET")
            .Select(m => m.Groups["method"].Value + " " + m.Groups["path"].Value)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            [
                "DELETE /users/{id}/aliases/{aliasId}",
                "POST /admin/debit",
                "POST /admin/faults",
                "POST /admin/reset",
                "POST /quotes",
                "POST /transactions",
                "POST /users/{id}/aliases",
                "POST /users/{id}/consent",
            ],
            writes);
        return writes;
    }

    /// <summary>A route pattern with its constraints dropped (<c>{id:int}</c> becomes <c>{id}</c>).</summary>
    private static string Normalise(string pattern)
    {
        var withSlash = pattern.StartsWith('/') ? pattern : "/" + pattern;
        return Constraint().Replace(withSlash, "{${name}}");
    }

    [GeneratedRegex(@"^\| `(?<method>GET|POST|PUT|PATCH|DELETE) (?<path>[^`?\s]+)(\?[^`]*)?` \|")]
    private static partial Regex TableRoute();

    [GeneratedRegex(@"\{(?<name>\w+):[^}]+\}")]
    private static partial Regex Constraint();
}
