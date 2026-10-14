extern alias AgentWeb;

using Agent.Core.Confirm;
using Agent.Core.Parsing;
using Agent.Core.Pending;
using Agent.Core.Pipeline;
using Agent.Core.Requests;
using Agent.Core.Tests.Agent;
using Agent.Core.Tests.Wallet;
using Agent.Core.Text;
using AgentWeb::Agent.Web.Resources;
using AgentWeb::Agent.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Wallet.MockApi.Data;

namespace Agent.Core.Tests.Web;

/// <summary>
/// The phone's session without a browser: <c>AddAgentWebServices</c> over the agent host's configuration, clock and
/// in-process wallet. One DI scope stands for one circuit.
/// </summary>
[Collection(AgentCollection.Name)]
public sealed class ChatSessionTests(AgentHost host) : IAsyncDisposable
{
    /// <summary>The English placeholder sentence of <c>scripted-sentences.json</c> (TODO-AR, Khalil's to replace).</summary>
    private const string HammoudiSentence = "send fifty thousand to my brother Hammoudi";

    private const string BalanceSentence = "balance";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private readonly List<IAsyncDisposable> disposables = [];

    private sealed record Circuit(AsyncServiceScope Scope, ServiceProvider Provider)
    {
        public ChatSession Session => Scope.ServiceProvider.GetRequiredService<ChatSession>();

        public TraceLog Trace => Scope.ServiceProvider.GetRequiredService<TraceLog>();

        public IStringLocalizer<PhoneStrings> Text => Scope.ServiceProvider.GetRequiredService<IStringLocalizer<PhoneStrings>>();
    }

    /// <summary>
    /// A circuit for <paramref name="userId"/>, with the embedded scripted sentences plus <paramref name="extra"/> ones
    /// (built from the seed through <see cref="SeedFacts"/>, never Arabic literals).
    /// </summary>
    private async Task<Circuit> StartAsync(int userId = SeedFacts.DemoUserId, params ScriptedSentence[] extra)
    {
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);
        await host.ResetAsync();

        var provider = host.BuildProvider((services, configuration) =>
        {
            services.AddAgentWebServices(configuration);
            services.Configure<AgentOptions>(o => o.DemoUserId = userId);
            if (extra.Length > 0)
            {
                var sentences = new List<ScriptedSentence>
                {
                    new(HammoudiSentence, ParsedRequest.Transfer(SeedFacts.Contact("S01").Alias, 50, AmountUnit.Thousand)),
                    new(BalanceSentence, ParsedRequest.Balance()),
                };
                sentences.AddRange(extra);
                services.AddSingleton<IParser>(new ScriptedParser(sentences));
            }
        });
        var scope = provider.CreateAsyncScope();
        disposables.Add(provider);
        disposables.Add(scope);
        return new Circuit(scope, provider);
    }

    public async ValueTask DisposeAsync()
    {
        for (var i = disposables.Count - 1; i >= 0; i--)
            await disposables[i].DisposeAsync();
    }

    private static async Task<PendingRecord> RecordAsync(Circuit circuit, Guid id, int userId = SeedFacts.DemoUserId)
    {
        await using var scope = circuit.Provider.CreateAsyncScope();
        var record = await scope.ServiceProvider.GetRequiredService<PendingStore>().FindAsync(id, userId, Token);
        Assert.NotNull(record);
        return record;
    }

    private async Task<int> WalletRowsAsync(string key)
    {
        await using var scope = host.Wallet.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
        return await db.Transactions.AsNoTracking().CountAsync(t => t.IdempotencyKey == key, Token);
    }

    private static ChatMessage LastAgentMessage(ChatSession session) => session.Messages.Last(m => m.Role == ChatRole.Agent);

    [Fact]
    public async Task TheHammoudiSentenceGivesTheCardAndConfirmMovesTheMoney()
    {
        var circuit = await StartAsync();
        var session = circuit.Session;

        await session.SendAsync(HammoudiSentence);

        var card = Assert.IsType<CardView>(session.Card);
        Assert.Equal(CardStatus.Live, session.CardState);
        Assert.Equal((50_000L, 1_000L, 51_000L, 699_000L), (card.AmountIqd, card.FeeIqd, card.TotalIqd, card.BalanceAfterIqd));
        Assert.Equal(SeedFacts.Contact("S01").Alias, card.DisplayName);
        Assert.False(card.FirstTime);
        Assert.Equal(ChatMessageKind.Card, LastAgentMessage(session).Kind);

        var outcome = await session.ConfirmAsync(null);

        var done = Assert.IsType<ConfirmOutcome.Done>(outcome);
        Assert.Equal(CardStatus.Done, session.CardState);
        var message = LastAgentMessage(session);
        Assert.Equal("done", message.Outcome);
        Assert.Equal(
            circuit.Text["Outcome_done", PhoneFormat.Digits(done.Reference), PhoneFormat.Amount(699_000, circuit.Text)].Value,
            message.Text);
        Assert.Equal(699_000, await host.Wallet.AccountBalanceAsync(SeedFacts.DemoPhone));
        Assert.Equal(1, session.WalletVersion);
    }

    [Fact]
    public async Task TypedYesPointsToConfirmAndMovesNothing()
    {
        var circuit = await StartAsync();
        var session = circuit.Session;
        await session.SendAsync(HammoudiSentence);
        var card = session.Card!;

        await session.SendAsync(circuit.Text["Word_yes"].Value);

        Assert.Equal(circuit.Text["Refusal_" + ReasonCodes.PointsToConfirm].Value, LastAgentMessage(session).Text);
        Assert.Equal(CardStatus.Live, session.CardState);
        var record = await RecordAsync(circuit, card.PendingId);
        Assert.Equal(PendingState.Pending, record.State);
        Assert.Equal(0, await WalletRowsAsync(record.IdempotencyKey));
        Assert.Equal(SeedFacts.DemoBalance, await host.Wallet.AccountBalanceAsync(SeedFacts.DemoPhone));
    }

    [Fact]
    public async Task TypedNoCancelsTheCard()
    {
        var circuit = await StartAsync();
        var session = circuit.Session;
        await session.SendAsync(HammoudiSentence);
        var card = session.Card!;

        await session.SendAsync(circuit.Text["Word_no"].Value);

        Assert.Equal(CardStatus.Cancelled, session.CardState);
        Assert.Equal("cancelled", LastAgentMessage(session).Outcome);
        Assert.Equal(PendingState.Cancelled, (await RecordAsync(circuit, card.PendingId)).State);
    }

    [Fact]
    public async Task ANewSentenceCancelsTheCardAndIsAnswered()
    {
        var circuit = await StartAsync();
        var session = circuit.Session;
        await session.SendAsync(HammoudiSentence);
        var card = session.Card!;

        await session.SendAsync(BalanceSentence);

        Assert.Equal(CardStatus.Cancelled, session.CardState);
        Assert.Equal(PendingState.Cancelled, (await RecordAsync(circuit, card.PendingId)).State);
        Assert.Contains(session.Messages, m => m.Outcome == "cancelled");
        Assert.Equal(
            circuit.Text["Reply_balance", PhoneFormat.Amount(SeedFacts.DemoBalance, circuit.Text)].Value,
            LastAgentMessage(session).Text);
    }

    [Fact]
    public async Task TwoConfirmsAtOnceMoveTheMoneyOnce()
    {
        var circuit = await StartAsync();
        var session = circuit.Session;
        await session.SendAsync(HammoudiSentence);
        var card = session.Card!;

        var outcomes = await Task.WhenAll(session.ConfirmAsync(null), session.ConfirmAsync(null));

        Assert.Single(outcomes, o => o is ConfirmOutcome.Done);
        Assert.Single(outcomes, o => o == new ConfirmOutcome.AlreadyHandled(PendingState.Done));
        Assert.Single(session.Messages, m => m.Outcome == "done");
        Assert.Single(session.Messages, m => m.Outcome == "already_handled");
        Assert.Equal(1, await WalletRowsAsync((await RecordAsync(circuit, card.PendingId)).IdempotencyKey));
        Assert.Equal(699_000, await host.Wallet.AccountBalanceAsync(SeedFacts.DemoPhone));
    }

    [Fact]
    public async Task TheTimerEndingMarksTheCardExpired()
    {
        var circuit = await StartAsync();
        var session = circuit.Session;
        await session.SendAsync(HammoudiSentence);
        var timer = circuit.Provider.GetRequiredService<IOptions<AgentOptions>>().Value.CardTimerSeconds;

        host.Clock.Advance(TimeSpan.FromSeconds(timer + 1));
        await session.RefreshAsync();

        Assert.Equal(CardStatus.Expired, session.CardState);
        Assert.Equal("expired", LastAgentMessage(session).Outcome);

        // A late Confirm (a stale page) still reaches nothing.
        Assert.IsType<ConfirmOutcome.Expired>(await session.ConfirmAsync(null));
        Assert.Equal(SeedFacts.DemoBalance, await host.Wallet.AccountBalanceAsync(SeedFacts.DemoPhone));
    }

    [Fact]
    public async Task AnOptionThatWasNotOfferedIsIgnored()
    {
        var circuit = await StartAsync(
            SeedFacts.DemoUserId,
            new ScriptedSentence("send fifty to hammoudi", ParsedRequest.Transfer(SeedFacts.Contact("S01").Alias, 50, AmountUnit.None)));
        var session = circuit.Session;

        // No question open: any tap is ignored.
        await session.TapOptionAsync("50000");
        Assert.Contains(circuit.Trace.Entries, e => e.Step == "option_rejected");

        await session.SendAsync("send fifty to hammoudi");
        var question = Assert.IsType<AgentReply.Question>(session.Question);
        Assert.Equal(QuestionKinds.WhichAmount, question.Kind);
        var before = session.Messages.Count;

        await session.TapOptionAsync("50000000");

        Assert.Equal(before, session.Messages.Count);
        Assert.Equal(2, circuit.Trace.Entries.Count(e => e.Step == "option_rejected"));
        Assert.Null(session.Card);

        // The offered option fills the amount and gives the card.
        await session.TapOptionAsync("50000");
        Assert.Equal(50_000, Assert.IsType<CardView>(session.Card).AmountIqd);
    }

    [Fact]
    public async Task ATypedAnswerFillsTheMissingAmount()
    {
        var circuit = await StartAsync(
            SeedFacts.DemoUserId,
            new ScriptedSentence("send to hammoudi", ParsedRequest.Transfer(SeedFacts.Contact("S01").Alias, null, AmountUnit.None)));
        var session = circuit.Session;

        await session.SendAsync("send to hammoudi");
        Assert.Equal(QuestionAnswer.Typed, Assert.IsType<AgentReply.Question>(session.Question).Answer);

        // Arabic-Indic digits are accepted (D20).
        await session.SendAsync(ArabicDigits.ToArabicIndic("20000"));

        Assert.Equal(20_000, Assert.IsType<CardView>(session.Card).AmountIqd);
    }

    [Fact]
    public async Task TwoActionsAtOnceDoNotCollide()
    {
        var circuit = await StartAsync();
        var session = circuit.Session;
        await session.SendAsync(HammoudiSentence);

        // A timer refresh and a typed message at the same moment: one at a time, each in its own scope.
        await Task.WhenAll(session.RefreshAsync(), session.SendAsync(BalanceSentence), session.RefreshAsync());

        Assert.DoesNotContain(circuit.Trace.Entries, e => e.Step == "error");
        Assert.Equal(
            circuit.Text["Reply_balance", PhoneFormat.Amount(SeedFacts.DemoBalance, circuit.Text)].Value,
            LastAgentMessage(session).Text);
        Assert.False(session.Busy);
    }

    [Fact]
    public async Task TheTraceKeepsNoPinKeyOrCodeAfterAPinPaymentAndAPurchase()
    {
        var circuit = await StartAsync(
            SeedFacts.TraderUserId,
            new ScriptedSentence("pay the trader", ParsedRequest.Transfer(SeedFacts.Contact("T01").SavedName, 1_100_000, AmountUnit.Dinars)),
            new ScriptedSentence("zain credit five", ParsedRequest.Purchase("zain", "credit", 5_000)));
        var session = circuit.Session;
        var pin = SeedFacts.PinOf("U3");

        await session.SendAsync("pay the trader");
        var pinCard = Assert.IsType<CardView>(session.Card);
        Assert.True(pinCard.PinRequired);
        Assert.True(session.PinVisible);
        Assert.IsType<ConfirmOutcome.Done>(await session.ConfirmAsync(ArabicDigits.ToArabicIndic(pin)));
        var pinKey = (await RecordAsync(circuit, pinCard.PendingId, SeedFacts.TraderUserId)).IdempotencyKey;

        await session.SendAsync("zain credit five");
        var purchaseCard = Assert.IsType<CardView>(session.Card);
        var bought = Assert.IsType<ConfirmOutcome.Done>(await session.ConfirmAsync(null));
        Assert.NotNull(bought.PurchaseCode);
        var purchaseKey = (await RecordAsync(circuit, purchaseCard.PendingId, SeedFacts.TraderUserId)).IdempotencyKey;

        // The orchestrator and the handler wrote to this circuit's trace, through each action's own scope.
        Assert.Contains(circuit.Trace.Entries, e => e.Step == "card_created");
        Assert.Contains(circuit.Trace.Entries, e => e.Step == "outcome:done");

        string[] secrets = [pin, ArabicDigits.ToArabicIndic(pin), pinKey, purchaseKey, bought.PurchaseCode];
        foreach (var entry in circuit.Trace.Entries)
        {
            foreach (var secret in secrets)
            {
                Assert.DoesNotContain(secret, entry.Step, StringComparison.Ordinal);
                Assert.DoesNotContain(secret, entry.Detail ?? string.Empty, StringComparison.Ordinal);
            }
        }
    }
}
