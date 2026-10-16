using Agent.Core.Confirm;
using Agent.Core.Pending;
using Agent.Core.Pipeline;
using Agent.Core.Requests;
using Agent.Core.Tests.Agent;
using Agent.Core.Tests.Wallet;
using Agent.Core.Trace;
using Microsoft.Extensions.DependencyInjection;

namespace Agent.Core.Tests.Trace;

/// <summary>
/// The live trace: the orchestrator writes its steps in the Code zone and the Confirm handler in the Write zone, and no
/// detail ever carries a PIN, an idempotency key, a purchase code or a phone number.
/// </summary>
[Collection(AgentCollection.Name)]
public class TraceSinkTests(AgentHost host)
{
    private const int U1 = SeedFacts.DemoUserId;
    private const int U3 = SeedFacts.TraderUserId;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task StartAsync()
    {
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);
        await host.ResetAsync();
    }

    /// <summary>A sink that keeps every step it is given.</summary>
    private sealed class RecordingSink : ITraceSink
    {
        public List<(TraceZone Zone, string Step, string? Detail)> Steps { get; } = [];

        public void Record(TraceZone zone, string step, string? detail) => Steps.Add((zone, step, detail));
    }

    private async Task<AgentReply> HandleAsync(RecordingSink sink, int userId, ParsedRequest request)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var orchestrator = ActivatorUtilities.CreateInstance<Orchestrator>(scope.ServiceProvider, sink);
        return await orchestrator.HandleAsync(userId, request, Token);
    }

    private async Task<ConfirmOutcome> ConfirmAsync(RecordingSink sink, PendingRecord card, string? pin = null)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var handler = ActivatorUtilities.CreateInstance<ConfirmHandler>(scope.ServiceProvider, sink);
        return await handler.ConfirmAsync(card.UserId, card.Id, pin, Token);
    }

    [Fact]
    public async Task NullSinkIsTheDefault()
    {
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);
        await using var scope = host.Services.CreateAsyncScope();

        Assert.IsType<NullTraceSink>(scope.ServiceProvider.GetRequiredService<ITraceSink>());
    }

    [Fact]
    public async Task ATransferTracesEveryStepInItsZone()
    {
        await StartAsync();
        var sink = new RecordingSink();

        var reply = await HandleAsync(sink, U1, ParsedRequest.Transfer(SeedFacts.Contact("S01").Alias, 50_000, AmountUnit.Dinars));
        var card = Assert.IsType<AgentReply.CardIssued>(reply).Pending;
        Assert.IsType<ConfirmOutcome.Done>(await ConfirmAsync(sink, card));

        Assert.Equal(
            [
                (TraceZone.Code, "parse_received"),
                (TraceZone.Code, "consent_checked"),
                (TraceZone.Code, "recipient_resolved"),
                (TraceZone.Code, "amount_resolved"),
                (TraceZone.Code, "checks_passed"),
                (TraceZone.Code, "card_created"),
                (TraceZone.Write, "begin_execute"),
                (TraceZone.Write, "recheck_ok"),
                (TraceZone.Write, "execute_sent"),
                (TraceZone.Write, "outcome:done"),
            ],
            sink.Steps.Select(s => (s.Zone, s.Step)));
        Assert.Equal("50000", sink.Steps.Single(s => s.Step == "amount_resolved").Detail);
    }

    [Fact]
    public async Task ARefusalAndAQuestionAreTraced()
    {
        await StartAsync();
        var sink = new RecordingSink();

        await HandleAsync(sink, U1, ParsedRequest.Transfer(SeedFacts.Contact("S01").Alias, null, AmountUnit.None));
        await HandleAsync(sink, U1, new ParsedRequest { Intent = RequestIntent.Unclear });
        await HandleAsync(sink, U1, ParsedRequest.Balance());

        var steps = sink.Steps.Select(s => s.Step).ToList();
        Assert.Contains("question:" + QuestionKinds.WhichAmount, steps);
        Assert.Contains("refusal:" + ReasonCodes.NotUnderstood, steps);
        Assert.Contains("balance_shown", steps);
        Assert.All(sink.Steps, s => Assert.Equal(TraceZone.Code, s.Zone));

        // The balance figure is not traced.
        Assert.Null(sink.Steps.Single(s => s.Step == "balance_shown").Detail);
    }

    [Fact]
    public async Task NoPinKeyCodeOrPhoneInAnyStep()
    {
        await StartAsync();
        var sink = new RecordingSink();
        var pin = SeedFacts.PinOf("U3");

        // A PIN payment of U3 (the PIN typed with Arabic-Indic digits, as the phone allows), then a purchase of U1.
        var pinReply = await HandleAsync(sink, U3, ParsedRequest.Transfer(SeedFacts.Contact("T01").SavedName, 1_100_000, AmountUnit.Dinars));
        var pinCard = Assert.IsType<AgentReply.CardIssued>(pinReply).Pending;
        Assert.True(pinCard.PinRequired);
        Assert.IsType<ConfirmOutcome.Done>(await ConfirmAsync(sink, pinCard, global::Agent.Core.Text.ArabicDigits.ToArabicIndic(pin)));

        var purchaseReply = await HandleAsync(sink, U1, ParsedRequest.Purchase("zain", "credit", 5_000));
        var purchaseCard = Assert.IsType<AgentReply.CardIssued>(purchaseReply).Pending;
        var bought = Assert.IsType<ConfirmOutcome.Done>(await ConfirmAsync(sink, purchaseCard));
        Assert.NotNull(bought.PurchaseCode);

        string[] secrets =
        [
            pin,
            global::Agent.Core.Text.ArabicDigits.ToArabicIndic(pin),
            pinCard.IdempotencyKey,
            purchaseCard.IdempotencyKey,
            bought.PurchaseCode,
            pinCard.ToPhone!,
            SeedFacts.DemoPhone,
            SeedFacts.Contact("T01").Number,
        ];
        Assert.NotEmpty(sink.Steps);
        foreach (var (zone, step, detail) in sink.Steps)
        {
            foreach (var secret in secrets)
            {
                Assert.DoesNotContain(secret, step, StringComparison.Ordinal);
                Assert.DoesNotContain(secret, detail ?? string.Empty, StringComparison.Ordinal);
            }
        }
    }
}
