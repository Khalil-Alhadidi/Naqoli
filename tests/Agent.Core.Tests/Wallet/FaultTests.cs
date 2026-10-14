using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Wallet.MockApi;
using Wallet.MockApi.Contracts;
using Wallet.MockApi.Data;

namespace Agent.Core.Tests.Wallet;

/// <summary>The fault switch (architecture, section 7): each mode, its effect on <c>POST /transactions</c>, and the admin key.</summary>
[Collection(WalletApiCollection.Name)]
public class FaultTests(WalletApiFixture fixture)
{
    private const string HammoudiPhone = SeedFacts.HammoudiPhone;
    private const int DemoUserId = SeedFacts.DemoUserId;
    private const long DemoSeedBalance = SeedFacts.DemoBalance;
    private const long HammoudiSeedBalance = 100_000;

    // At least 16 characters (WalletOptions.MinAdminKeyLength). A test value, not a secret.
    private const string AdminTestKey = "test-admin-key-0123456789";
    private const string ProductionClientKey = "test-client-key-0123456789";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static PaymentRequest SendFiftyThousandToHammoudi
        => new(DemoUserId, "transfer", 50_000, HammoudiPhone, null, null, null, null);

    [Fact]
    public async Task ErrorFaultReturns500AndMovesNothing()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await fixture.ResetAsync();
        await SetFaultAsync(new FaultRequest("error", null));

        var response = await PostAsync(fixture.Client, "f-error", SendFiftyThousandToHammoudi);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("injected_error", (await response.ReadProblemAsync(Token)).Code());
        Assert.Equal(DemoSeedBalance, await BalanceAsync(DemoUserId));
        Assert.Equal(HammoudiSeedBalance, await fixture.AccountBalanceAsync(HammoudiPhone));
        Assert.Equal(HttpStatusCode.NotFound, (await fixture.Client.GetAsync($"/transactions/by-key/f-error?userId={DemoUserId}", Token)).StatusCode);
    }

    [Fact]
    public async Task SlowFaultDelaysButSucceeds()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await fixture.ResetAsync();
        var state = await SetFaultAsync(new FaultRequest("slow", 300));
        Assert.Equal(new FaultState("slow", 300, false), state);

        var watch = Stopwatch.StartNew();
        var response = await PostAsync(fixture.Client, "f-slow", SendFiftyThousandToHammoudi);
        watch.Stop();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(watch.ElapsedMilliseconds >= 300, $"The slow fault answered after {watch.ElapsedMilliseconds} ms.");
        Assert.Equal(699_000, await BalanceAsync(DemoUserId));
    }

    [Fact]
    public async Task TimeoutBeforeExecuteMovesNothingAndByKeyIs404()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await fixture.ResetAsync();
        await SetFaultAsync(new FaultRequest("timeoutBeforeExecute", 200));

        var response = await PostAsync(fixture.Client, "f-before", SendFiftyThousandToHammoudi);

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.Equal("injected_timeout", (await response.ReadProblemAsync(Token)).Code());
        Assert.Equal(DemoSeedBalance, await BalanceAsync(DemoUserId));
        Assert.Equal(HammoudiSeedBalance, await fixture.AccountBalanceAsync(HammoudiPhone));

        var lookup = await fixture.Client.GetAsync($"/transactions/by-key/f-before?userId={DemoUserId}", Token);
        Assert.Equal(HttpStatusCode.NotFound, lookup.StatusCode);
        Assert.Equal("unknown_key", (await lookup.ReadProblemAsync(Token)).Code());
    }

    [Fact]
    public async Task TimeoutAfterExecuteIsFoundByKeyWhileTheRequestStillHangs()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await fixture.ResetAsync();
        await SetFaultAsync(new FaultRequest("timeoutAfterExecute", 5_000, Once: true));

        using (var impatient = fixture.CreateClient())
        {
            impatient.Timeout = TimeSpan.FromSeconds(1);
            await Assert.ThrowsAnyAsync<TaskCanceledException>(
                () => PostAsync(impatient, "f-after", SendFiftyThousandToHammoudi));
        }

        // The original request is still waiting on the server; the money has moved and the key says so.
        var lookup = await fixture.Client.GetFromJsonAsync<TransactionResponse>($"/transactions/by-key/f-after?userId={DemoUserId}", Token);
        Assert.NotNull(lookup);
        Assert.Equal("done", lookup.Status);
        Assert.Equal(699_000, lookup.BalanceAfterIqd);
        Assert.Equal(699_000, await BalanceAsync(DemoUserId));
        Assert.Equal(HammoudiSeedBalance + 50_000, await fixture.AccountBalanceAsync(HammoudiPhone));

        await SetFaultAsync(new FaultRequest("none", null));
    }

    [Fact]
    public async Task OnceFaultClearsAfterOneRequest()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await fixture.ResetAsync();
        await SetFaultAsync(new FaultRequest("error", null, Once: true));

        var first = await PostAsync(fixture.Client, "f-once", SendFiftyThousandToHammoudi);
        Assert.Equal(HttpStatusCode.InternalServerError, first.StatusCode);

        var state = await fixture.Client.GetFromJsonAsync<FaultState>("/admin/faults", Token);
        Assert.Equal("none", state!.Mode);

        // The retry reuses the key and goes through.
        var retry = await PostAsync(fixture.Client, "f-once", SendFiftyThousandToHammoudi);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.Equal(699_000, await BalanceAsync(DemoUserId));
    }

    [Fact]
    public async Task UnknownFaultModeIs422()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await fixture.ResetAsync();

        var response = await fixture.Client.PostAsJsonAsync("/admin/faults", new FaultRequest("explode", null), Token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("invalid_fault_mode", (await response.ReadProblemAsync(Token)).Code());
        Assert.Equal("none", (await fixture.Client.GetFromJsonAsync<FaultState>("/admin/faults", Token))!.Mode);
    }

    [Fact]
    public async Task FaultDelayDefaultsToTheConfiguredValue()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await fixture.ResetAsync();

        var state = await SetFaultAsync(new FaultRequest("timeoutBeforeExecute", null));

        Assert.Equal(new FaultState("timeoutBeforeExecute", 60_000, false), state);
        await SetFaultAsync(new FaultRequest("none", null));
    }

    [Fact]
    public async Task AdminKeyRequiredWhenConfigured()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);

        await using var keyed = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:Wallet", fixture.TestConnectionString);
            host.UseSetting("Wallet:AdminKey", AdminTestKey);
            host.UseSetting("Wallet:ClientKey", string.Empty);
        });
        using var client = keyed.CreateClient();
        client.DefaultRequestHeaders.Add(ClientKeyFilter.HeaderName, WalletApiFixture.DevelopmentClientHeaderValue);

        var without = await client.GetAsync("/admin/faults", Token);
        Assert.Equal(HttpStatusCode.Forbidden, without.StatusCode);
        Assert.Equal("admin_key_required", (await without.ReadProblemAsync(Token)).Code());

        using var wrong = new HttpRequestMessage(HttpMethod.Get, "/admin/faults");
        wrong.Headers.Add("X-Wallet-Admin-Key", "not-the-key");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(wrong, Token)).StatusCode);

        using var right = new HttpRequestMessage(HttpMethod.Get, "/admin/faults");
        right.Headers.Add("X-Wallet-Admin-Key", AdminTestKey);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(right, Token)).StatusCode);

        // Every admin route is behind the key, the reset included.
        var reset = await client.PostAsync("/admin/reset", null, Token);
        Assert.Equal(HttpStatusCode.Forbidden, reset.StatusCode);
        var setFault = await client.PostAsJsonAsync("/admin/faults", new FaultRequest("error", null), Token);
        Assert.Equal(HttpStatusCode.Forbidden, setFault.StatusCode);
    }

    [Fact]
    public async Task AdminIsClosedOutsideDevelopmentWithoutKey()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);

        await using var production = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseEnvironment("Production");
            host.UseSetting("ConnectionStrings:Wallet", fixture.TestConnectionString);
            host.UseSetting("Wallet:AdminKey", string.Empty);
            // A configured client key, sent, so the admin filter is the one that answers.
            host.UseSetting("Wallet:ClientKey", ProductionClientKey);
        });
        using var client = production.CreateClient();
        client.DefaultRequestHeaders.Add(ClientKeyFilter.HeaderName, ProductionClientKey);
        client.DefaultRequestHeaders.Add(AdminKeyFilter.HeaderName, "any-value");

        // Closed on every admin route, even with the header: outside Development only a configured key opens them.
        var faults = await client.GetAsync("/admin/faults", Token);
        Assert.Equal(HttpStatusCode.Forbidden, faults.StatusCode);
        Assert.Equal("admin_key_required", (await faults.ReadProblemAsync(Token)).Code());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/admin/faults", new FaultRequest("error", null), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/admin/reset", null, Token)).StatusCode);
    }

    [Fact]
    public async Task AdminNeedsTheHeaderInDevelopmentWithoutKey()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await fixture.ResetAsync();

        // A client without the header, as a web page's plain fetch would be: refused, so nothing is reset.
        using var bare = fixture.CreateClient();
        var reset = await bare.PostAsync("/admin/reset", null, Token);
        Assert.Equal(HttpStatusCode.Forbidden, reset.StatusCode);
        Assert.Equal("admin_key_required", (await reset.ReadProblemAsync(Token)).Code());
        Assert.Equal(HttpStatusCode.Forbidden, (await bare.GetAsync("/admin/faults", Token)).StatusCode);

        // The fixture's client sends the header with an arbitrary value, and is let in.
        Assert.Equal(HttpStatusCode.OK, (await fixture.Client.GetAsync("/admin/faults", Token)).StatusCode);
    }

    private async Task<FaultState> SetFaultAsync(FaultRequest request)
    {
        var response = await fixture.Client.PostAsJsonAsync("/admin/faults", request, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var state = await response.Content.ReadFromJsonAsync<FaultState>(Token);
        Assert.NotNull(state);
        return state;
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string key, PaymentRequest request)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/transactions")
        {
            Content = JsonContent.Create(request),
        };
        message.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(message, Token);
    }

    private async Task<long> BalanceAsync(int userId)
    {
        var balance = await fixture.Client.GetFromJsonAsync<BalanceResponse>($"/users/{userId}/balance", Token);
        return balance!.BalanceIqd;
    }
}
