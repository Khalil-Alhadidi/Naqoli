extern alias AgentWeb;

using Agent.Core.Confirm;
using Agent.Core.Conversation;
using Agent.Core.Parsing;
using Agent.Core.Pipeline;
using Agent.Core.Requests;
using Agent.Core.Tests.Agent;
using Agent.Core.Tests.Wallet;
using AgentWeb::Agent.Web.Resources;
using AgentWeb::Agent.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Agent.Core.Tests.Web;

/// <summary>
/// The phone's adapter without a browser: <c>AddAgentWebServices</c> over the agent host's configuration, clock and
/// in-process wallet. One DI scope stands for one circuit. The conversation's own behaviour is tested in
/// <c>ConversationTests</c>; these cases cover what the adapter adds: localised text, the resource file's D6 words,
/// message ids, <c>Changed</c>, and the circuit's trace.
/// </summary>
[Collection(AgentCollection.Name)]
public sealed class ChatSessionTests(AgentHost host) : IAsyncDisposable
{
    /// <summary>The English placeholder sentence of <c>scripted-sentences.json</c> (TODO-AR, Khalil's to replace).</summary>
    private const string HammoudiSentence = "send fifty thousand to my brother Hammoudi";

    private const string BalanceSentence = "balance";

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

    private static ChatMessage LastAgentMessage(ChatSession session) => session.Messages.Last(m => m.Role == ChatRole.Agent);

    [Fact]
    public async Task TheConversationsCodesBecomeLocalisedMessages()
    {
        var circuit = await StartAsync();
        var session = circuit.Session;

        await session.SendAsync(HammoudiSentence);

        // The user's words as typed, then the card's message with the card under it.
        Assert.Equal(new ChatMessage(1, ChatRole.User, HammoudiSentence, ChatMessageKind.Text), session.Messages[0]);
        var cardMessage = LastAgentMessage(session);
        Assert.Equal(new ChatMessage(2, ChatRole.Agent, circuit.Text["Card_Title"].Value, ChatMessageKind.Card), cardMessage);
        Assert.Equal(cardMessage.Id, session.CardMessageId);
        Assert.Equal(CardStatus.Live, session.CardState);

        // The yes word of the resource file reaches the conversation (D6): it points to the button.
        await session.SendAsync(circuit.Text["Word_yes"].Value);
        Assert.Equal(circuit.Text["Refusal_" + ReasonCodes.PointsToConfirm].Value, LastAgentMessage(session).Text);
        Assert.Equal(CardStatus.Live, session.CardState);

        var done = Assert.IsType<ConfirmOutcome.Done>(await session.ConfirmAsync(null));

        var message = LastAgentMessage(session);
        Assert.Equal("done", message.Outcome);
        Assert.Equal(
            circuit.Text["Outcome_done", PhoneFormat.Digits(done.Reference), PhoneFormat.Amount(699_000, circuit.Text)].Value,
            message.Text);
        Assert.Equal(699_000, await host.Wallet.AccountBalanceAsync(SeedFacts.DemoPhone));
        Assert.Equal(1, session.WalletVersion);
        Assert.Equal(session.Messages.Select(m => m.Id), Enumerable.Range(1, session.Messages.Count));
    }

    [Fact]
    public async Task ChangedIsRaisedAndTheAgentWritesToTheCircuitsTrace()
    {
        var circuit = await StartAsync(
            SeedFacts.DemoUserId,
            new ScriptedSentence("send fifty to hammoudi", ParsedRequest.Transfer(SeedFacts.Contact("S01").Alias, 50, AmountUnit.None)));
        var session = circuit.Session;
        var raised = 0;
        session.Changed += () => Interlocked.Increment(ref raised);

        await session.SendAsync("send fifty to hammoudi");

        Assert.True(raised > 0);
        Assert.Equal(circuit.Text["Question_" + QuestionKinds.WhichAmount].Value, LastAgentMessage(session).Text);
        Assert.Equal(LastAgentMessage(session).Id, session.QuestionMessageId);

        // A tapped option shows the option's own text.
        await session.TapOptionAsync("50000");
        Assert.Contains(session.Messages, m => m.Role == ChatRole.User && m.Text == PhoneFormat.Amount(50_000, circuit.Text));

        // The orchestrator wrote to this circuit's trace through each action's own scope (TraceLog.Current).
        Assert.Contains(circuit.Trace.Entries, e => e.Step == "card_created");
        Assert.Contains(circuit.Trace.Entries, e => e.Step == "message_received");
    }
}
