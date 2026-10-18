extern alias AgentWeb;

using Agent.Core.Parsing;
using Agent.Core.Pipeline;
using Agent.Core.Requests;
using Agent.Core.Tests.Agent;
using Agent.Core.Tests.Wallet;
using AgentWeb::Agent.Web.Resources;
using AgentWeb::Agent.Web.Services;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Wallet.MockApi.Contracts;

namespace Agent.Core.Tests.Web;

/// <summary>
/// The fault panel's services (milestone 8) as <c>AddAgentWebServices</c> registers them, over the agent host's
/// in-process wallet: the parser is wrapped in <see cref="FaultingParser"/>, an armed model fault reaches the phone as
/// <c>model_unavailable</c> once, and the console's side door sets, reads back and clears the wallet's fault switch and
/// takes an external debit, and resets the wallet (the reset panel, milestone 7 post-tag). The side door names only
/// <c>admin/faults</c>, <c>admin/debit</c> and <c>admin/reset</c>.
/// </summary>
[Collection(AgentCollection.Name)]
public sealed class FaultPanelServicesTests(AgentHost host) : IAsyncDisposable
{
    private const string HammoudiSentence = "send fifty thousand to my brother Hammoudi";

    private readonly List<IAsyncDisposable> disposables = [];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    /// The web services. With <paramref name="scripted"/> the test's own scripted parser is registered after them and
    /// wrapped again by <see cref="ServiceCollectionExtensions.AddFaultingParser"/>, as the console wraps the parser
    /// <c>Agent:Parser</c> names.
    /// </summary>
    private async Task<AsyncServiceScope> StartAsync(bool scripted)
    {
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);
        await host.ResetAsync();

        var provider = host.BuildProvider((services, configuration) =>
        {
            services.AddAgentWebServices(configuration);

            // The test provider has no host environment, so it counts as outside Development: a real-looking key
            // (review R2). The in-process wallet has no admin key, so any non-empty value is let in.
            services.Configure<ConsoleOptions>(o => o.WalletAdminKey = "fault-panel-tests-admin-key");
            services.AddHttpClient<WalletFaults>().ConfigurePrimaryHttpMessageHandler(() => host.Wallet.CreateHandler());
            if (scripted)
            {
                services.AddSingleton<IParser>(new ScriptedParser(
                [
                    new(HammoudiSentence, ParsedRequest.Transfer(SeedFacts.Contact("S01").Alias, 50, AmountUnit.Thousand)),
                ]));
                services.AddFaultingParser();
            }
        });
        var scope = provider.CreateAsyncScope();
        disposables.Add(provider);
        disposables.Add(scope);
        return scope;
    }

    public async ValueTask DisposeAsync()
    {
        for (var i = disposables.Count - 1; i >= 0; i--)
            await disposables[i].DisposeAsync();
    }

    [Fact]
    public async Task TheRegisteredParserIsTheFaultingParserOverTheScriptedParser()
    {
        var scope = await StartAsync(scripted: false);

        var parser = Assert.IsType<FaultingParser>(scope.ServiceProvider.GetRequiredService<IParser>());
        Assert.IsType<ScriptedParser>(parser.Inner);
        Assert.Equal(ScriptedParser.ParserName, parser.Name);
        Assert.Same(parser, scope.ServiceProvider.GetRequiredService<IParser>());
    }

    [Fact]
    public async Task AnArmedUnreachableFaultRepliesModelUnavailableOnceThenTheNextMessageParses()
    {
        var scope = await StartAsync(scripted: true);
        var session = scope.ServiceProvider.GetRequiredService<ChatSession>();
        var text = scope.ServiceProvider.GetRequiredService<IStringLocalizer<PhoneStrings>>();
        var faults = scope.ServiceProvider.GetRequiredService<ModelFaultSwitch>();
        faults.Set(ModelFaultMode.Unreachable, once: true);

        await session.SendAsync(HammoudiSentence);

        Assert.Equal(text["Refusal_" + ReasonCodes.ModelUnavailable].Value, session.Messages.Last(m => m.Role == ChatRole.Agent).Text);
        Assert.Null(session.CardMessageId);
        Assert.Equal(ModelFaultMode.None, faults.Mode);

        await session.SendAsync(HammoudiSentence);

        Assert.NotNull(session.CardMessageId);
    }

    [Fact]
    public async Task TheSideDoorSetsReadsBackAndClearsTheWalletsFaultSwitch()
    {
        var scope = await StartAsync(scripted: false);
        var faults = scope.ServiceProvider.GetRequiredService<WalletFaults>();

        var set = await faults.SetAsync("timeoutAfterExecute", 4_000, once: true, Token);
        Assert.Equal(new WalletFaultState("timeoutAfterExecute", 4_000, true), Assert.IsType<WalletFaultResult.Ok>(set).State);
        Assert.Equal(new WalletFaultState("timeoutAfterExecute", 4_000, true), Assert.IsType<WalletFaultResult.Ok>(await faults.GetAsync(Token)).State);

        var cleared = Assert.IsType<WalletFaultResult.Ok>(await faults.ClearAsync(Token));
        Assert.Equal("none", cleared.State.Mode);

        var refused = Assert.IsType<WalletFaultResult.Failed>(await faults.SetAsync("meltdown", null, once: true, Token));
        Assert.Equal("invalid_fault_mode", refused.Problem);
    }

    [Fact]
    public async Task TheSideDoorTakesAnExternalDebit()
    {
        var scope = await StartAsync(scripted: false);
        var faults = scope.ServiceProvider.GetRequiredService<WalletFaults>();
        var raised = Record(scope);

        var debit = Assert.IsType<WalletDebitResult.Done>(await faults.DebitAsync(SeedFacts.DemoUserId, 5_000, Token));
        Assert.StartsWith("ZC-", debit.Reference, StringComparison.Ordinal);
        Assert.Equal(SeedFacts.DemoBalance - 5_000, debit.BalanceAfterIqd);
        Assert.Equal(SeedFacts.DemoBalance - 5_000, await host.Wallet.AccountBalanceAsync(SeedFacts.DemoPhone));

        // The panels that mirror the wallet hear of the debit (milestone 7 post-tag); a refused one changed nothing.
        Assert.Equal(new WalletChange(SeedFacts.DemoUserId, WalletChangeKind.Payment), Assert.Single(raised));

        var refused = Assert.IsType<WalletDebitResult.Failed>(await faults.DebitAsync(SeedFacts.DemoUserId, SeedFacts.DemoBalance, Token));
        Assert.Equal("insufficient_funds", refused.Problem);
        Assert.Single(raised);
    }

    /// <summary>The changes raised on the provider's <see cref="WalletChanges"/> from now on.</summary>
    private static List<WalletChange> Record(AsyncServiceScope scope)
    {
        var raised = new List<WalletChange>();
        scope.ServiceProvider.GetRequiredService<WalletChanges>().Changed += change =>
        {
            lock (raised)
                raised.Add(change);
        };
        return raised;
    }

    [Fact]
    public async Task TheSideDoorResetsTheWalletWithEveryUserNotConsentedAndReportsTheCounts()
    {
        var scope = await StartAsync(scripted: false);
        var faults = scope.ServiceProvider.GetRequiredService<WalletFaults>();
        try
        {
            Assert.IsType<WalletDebitResult.Done>(await faults.DebitAsync(SeedFacts.DemoUserId, 5_000, Token));
            Assert.IsType<WalletFaultResult.Ok>(await faults.SetAsync("error", null, once: false, Token));
            var raised = Record(scope);

            var reset = Assert.IsType<WalletResetResult.Ok>(await faults.ResetAsync(Token));

            // Every tab's wallet panels hear of the reset, for every user (milestone 7 post-tag).
            Assert.Equal(new WalletChange(WalletChange.EveryUser, WalletChangeKind.Reset), Assert.Single(raised));

            Assert.Equal(new WalletResetCounts(5, 42, 34, 9, 11, 15, 86, 9), reset.Counts);
            Assert.Equal(SeedFacts.DemoBalance, await host.Wallet.AccountBalanceAsync(SeedFacts.DemoPhone));
            Assert.Equal("none", Assert.IsType<WalletFaultResult.Ok>(await faults.GetAsync(Token)).State.Mode);

            // No query: the console's reset clears every user's consent (Khalil, 18 Oct 2026).
            var demo = await host.Wallet.Client.GetFromJsonAsync<UserSummary>($"/users/{SeedFacts.DemoUserId}", Token);
            Assert.False(demo!.ConsentGiven);
        }
        finally
        {
            // Back to the seed's consent for the rest of the collection.
            await host.ResetAsync();
        }
    }
}
