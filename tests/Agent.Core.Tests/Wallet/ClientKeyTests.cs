using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Wallet.MockApi;
using Wallet.MockApi.Contracts;

namespace Agent.Core.Tests.Wallet;

/// <summary>
/// The <c>X-Wallet-Client-Key</c> filter on every wallet route except <c>/health</c> (milestone 2 review I3,
/// milestone 2b review I1).
/// </summary>
[Collection(WalletApiCollection.Name)]
public class ClientKeyTests(WalletApiFixture fixture)
{
    private const string ConfiguredClientKey = "0123456789abcdef";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static PaymentRequest SendFiftyThousandToHammoudi
        => new(SeedFacts.DemoUserId, "transfer", 50_000, SeedFacts.HammoudiPhone, null, null, null, null);

    [Fact]
    public async Task NoHeaderIs403OnEveryRouteExceptHealth()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await fixture.ResetAsync();

        using var bare = BareClient();

        var health = await bare.GetAsync("/health", Token);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        await AssertClientKeyRequiredAsync(await bare.GetAsync("/users/1", Token));
        await AssertClientKeyRequiredAsync(await bare.GetAsync("/store/products", Token));
        await AssertClientKeyRequiredAsync(await bare.PostAsJsonAsync("/quotes", SendFiftyThousandToHammoudi, Token));
        await AssertClientKeyRequiredAsync(await bare.GetAsync("/transactions/by-key/x?userId=1", Token));

        // The admin header alone is not enough: the client key comes first.
        using var reset = new HttpRequestMessage(HttpMethod.Post, "/admin/reset");
        reset.Headers.Add(AdminKeyFilter.HeaderName, WalletApiFixture.DevelopmentAdminHeaderValue);
        await AssertClientKeyRequiredAsync(await bare.SendAsync(reset, Token));
    }

    [Fact]
    public async Task HeaderWithAnyValueOpensInDevelopment()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await fixture.ResetAsync();

        using var bare = BareClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/users/1");
        request.Headers.Add(ClientKeyFilter.HeaderName, "anything");

        Assert.Equal(HttpStatusCode.OK, (await bare.SendAsync(request, Token)).StatusCode);
    }

    [Fact]
    public async Task ConfiguredKeyMustMatch()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await fixture.ResetAsync();

        await using var keyed = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:Wallet", fixture.TestConnectionString);
            host.UseSetting("Wallet:AdminKey", string.Empty);
            host.UseSetting("Wallet:ClientKey", ConfiguredClientKey);
        });
        using var client = keyed.CreateClient();

        await AssertClientKeyRequiredAsync(await client.GetAsync("/users/1", Token));

        using var wrong = new HttpRequestMessage(HttpMethod.Get, "/users/1");
        wrong.Headers.Add(ClientKeyFilter.HeaderName, "not-the-client-key");
        await AssertClientKeyRequiredAsync(await client.SendAsync(wrong, Token));

        using var right = new HttpRequestMessage(HttpMethod.Get, "/users/1");
        right.Headers.Add(ClientKeyFilter.HeaderName, ConfiguredClientKey);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(right, Token)).StatusCode);
    }

    [Fact]
    public async Task ProductionWithoutKeyIsClosed()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);

        await using var production = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseEnvironment("Production");
            host.UseSetting("ConnectionStrings:Wallet", fixture.TestConnectionString);
            host.UseSetting("Wallet:AdminKey", string.Empty);
            host.UseSetting("Wallet:ClientKey", string.Empty);
        });
        using var client = production.CreateClient();
        client.DefaultRequestHeaders.Add(ClientKeyFilter.HeaderName, "any-value");
        client.DefaultRequestHeaders.Add(AdminKeyFilter.HeaderName, "any-value");

        // Outside Development only a configured key opens the routes, whatever header is sent.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health", Token)).StatusCode);
        await AssertClientKeyRequiredAsync(await client.GetAsync("/users/1", Token));
        await AssertClientKeyRequiredAsync(await client.PostAsJsonAsync("/quotes", SendFiftyThousandToHammoudi, Token));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/admin/faults", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/admin/reset", null, Token)).StatusCode);
    }

    // A client of the fixture's API with no headers at all.
    private HttpClient BareClient()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Remove(ClientKeyFilter.HeaderName);
        return client;
    }

    private static async Task AssertClientKeyRequiredAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("client_key_required", (await response.ReadProblemAsync(Token)).Code());
    }
}
