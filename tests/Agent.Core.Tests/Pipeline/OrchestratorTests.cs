using System.Text.Json;
using Agent.Core.Audit;
using Agent.Core.Confirm;
using Agent.Core.Data;
using Agent.Core.Pending;
using Agent.Core.Pipeline;
using Agent.Core.Requests;
using Agent.Core.Resolver;
using Agent.Core.Tests.Agent;
using Agent.Core.Tests.Wallet;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Agent.Core.Tests.Pipeline;

/// <summary>
/// The orchestrator against the agent's in-process wallet: one parsed request in, one reply out (a card, a question, a
/// refusal, the balance or the consent screen), with the audit row each writes.
/// </summary>
[Collection(AgentCollection.Name)]
public class OrchestratorTests(AgentHost host)
{
    private const int U1 = SeedFacts.DemoUserId;
    private const int U2 = SeedFacts.TightUserId;
    private const int U3 = SeedFacts.TraderUserId;

    /// <summary>U5: has not given consent yet.</summary>
    private const int U5 = 5;

    /// <summary>An account no contact of U1 points to (X01).</summary>
    private const string NewPhone = "07805550190";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string HammoudiAlias => SeedFacts.Contact("S01").Alias!;

    private async Task StartAsync()
    {
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);
        await host.ResetAsync();
    }

    private async Task<AgentReply> HandleAsync(int userId, ParsedRequest request)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<Orchestrator>().HandleAsync(userId, request, Token);
    }

    private async Task<AgentReply> HandleAsync(int userId, ParsedRequest request, SlotAnswers answers)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<Orchestrator>().HandleAsync(userId, request, answers, Token);
    }

    private async Task<List<AuditEntry>> AuditAsync()
    {
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
        return await db.AuditEntries.AsNoTracking().OrderBy(a => a.Id).ToListAsync(Token);
    }

    private static PendingRecord Card(AgentReply reply) => Assert.IsType<AgentReply.CardIssued>(reply).Pending;

    private static AgentReply.Refusal Refusal(AgentReply reply, string code)
    {
        var refusal = Assert.IsType<AgentReply.Refusal>(reply);
        Assert.Equal(code, refusal.Code);
        return refusal;
    }

    private static AgentReply.Question Question(AgentReply reply, string kind, QuestionAnswer answer)
    {
        var question = Assert.IsType<AgentReply.Question>(reply);
        Assert.Equal(kind, question.Kind);
        Assert.Equal(answer, question.Answer);
        return question;
    }

    /// <summary>The typed text of the first example of <paramref name="rule"/> in the matching examples, for U1.</summary>
    private static string ExampleTyped(int rule, string? option = null)
    {
        var path = Path.Combine(SeedHashTests.RepositoryRoot(), "tests", "Agent.Core.Tests", "Resolver", "matching-examples.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.EnumerateArray()
            .First(e => e.GetProperty("rule").GetInt32() == rule
                && e.GetProperty("user").GetString() == "U1"
                && (option is null || e.GetProperty("options").EnumerateArray().Select(o => o.GetString()).SequenceEqual([option])))
            .GetProperty("typed").GetString()!;
    }

    /// <summary>The amount of the most recent seeded transfer of a contact (the wallet's "last sent").</summary>
    private static long LastSentOf(string contactCode)
        => SeedFacts.Seed.History
            .Where(h => h.Contact == contactCode)
            .OrderBy(h => h.DaysAgo)
            .ThenByDescending(h => h.Time, StringComparer.Ordinal)
            .First().Amount;

    [Fact]
    public async Task CleanTransferGivesACard()
    {
        await StartAsync();

        var card = Card(await HandleAsync(U1, ParsedRequest.Transfer(HammoudiAlias, 50_000, AmountUnit.Dinars)));

        Assert.Equal("transfer", card.Type);
        Assert.Equal(50_000, card.AmountIqd);
        Assert.Equal(1_000, card.FeeIqd);
        Assert.Equal(51_000, card.TotalIqd);
        Assert.Equal(SeedFacts.DemoBalance, card.QuotedBalanceIqd);
        Assert.Equal(HammoudiAlias, card.DisplayName);
        Assert.Equal(SeedFacts.Account(SeedFacts.HammoudiPhone).RegisteredName, card.RegisteredName);
        Assert.Equal(SeedFacts.HammoudiPhone, card.ToPhone);
        Assert.NotNull(card.ToAccountId);
        Assert.False(card.FirstTime);
        Assert.False(card.PinRequired);
        Assert.Null(card.Warning);
        Assert.Equal(PendingState.Pending, card.State);
        Assert.Equal(32, card.IdempotencyKey.Length);
        Assert.Equal(TimeSpan.FromSeconds(45), card.ExpiresAt - card.CreatedAt);

        var created = Assert.Single(await AuditAsync());
        Assert.Equal(AuditEvents.Created, created.Event);
        Assert.Equal(card.Id, created.PendingId);
    }

    [Fact]
    public async Task AmbiguousNameAsks()
    {
        await StartAsync();

        var reply = await HandleAsync(U1, ParsedRequest.Transfer(SeedFacts.Contact("S08").SavedName, 50_000, AmountUnit.Dinars));

        var question = Question(reply, QuestionKinds.WhichContact, QuestionAnswer.Tap);
        Assert.Equal(["S08", "S09"], question.Options.Select(o => o.Value).Order(StringComparer.Ordinal));
        foreach (var option in question.Options)
        {
            Assert.Equal(SeedFacts.Contact(option.Value).SavedName, option.Display);
            Assert.Equal(SeedFacts.Account(SeedFacts.Contact(option.Value).Number).RegisteredName, option.RegisteredName);
            Assert.NotNull(option.LastSentAt);
            Assert.Equal(LastSentOf(option.Value), option.LastSentAmountIqd);
        }

        var asked = Assert.Single(await AuditAsync());
        Assert.Equal(AuditEvents.QuestionAsked, asked.Event);
        Assert.Equal(QuestionKinds.WhichContact, asked.Reason);
    }

    [Fact]
    public async Task NearNameAsksDidYouMean()
    {
        await StartAsync();

        var reply = await HandleAsync(U1, ParsedRequest.Transfer(ExampleTyped(6, "S01"), 50_000, AmountUnit.Dinars));

        var question = Question(reply, QuestionKinds.DidYouMean, QuestionAnswer.Tap);
        Assert.Equal("S01", Assert.Single(question.Options).Value);
    }

    [Fact]
    public async Task UnknownNameIsRefused()
    {
        await StartAsync();

        var typed = ExampleTyped(8);
        var refusal = Refusal(await HandleAsync(U1, ParsedRequest.Transfer(typed, 50_000, AmountUnit.Dinars)), ReasonCodes.RecipientNotFound);
        Assert.Equal(typed, refusal.Detail);

        var refused = Assert.Single(await AuditAsync());
        Assert.Equal(AuditEvents.Refused, refused.Event);
        Assert.Equal(ReasonCodes.RecipientNotFound, refused.Reason);
    }

    [Fact]
    public async Task TypedPhoneGivesFirstTimeCard()
    {
        await StartAsync();

        var card = Card(await HandleAsync(U1, ParsedRequest.Transfer(NewPhone, 20_000, AmountUnit.Dinars)));

        Assert.Equal(NewPhone, card.DisplayName);
        Assert.Equal(NewPhone, card.ToPhone);
        Assert.Equal(SeedFacts.Account(NewPhone).RegisteredName, card.RegisteredName);
        Assert.True(card.FirstTime);
        Assert.Equal(21_000, card.TotalIqd);
    }

    [Fact]
    public async Task IncompleteNumberIsRefused()
    {
        await StartAsync();
        Refusal(await HandleAsync(U1, ParsedRequest.Transfer("078055501", 20_000, AmountUnit.Dinars)), ReasonCodes.InvalidNumber);
    }

    [Fact]
    public async Task NoWalletNumberIsRefused()
    {
        await StartAsync();
        Refusal(await HandleAsync(U1, ParsedRequest.Transfer("07805559999", 20_000, AmountUnit.Dinars)), ReasonCodes.NumberHasNoWallet);
    }

    [Fact]
    public async Task ClosedAccountIsRefused()
    {
        await StartAsync();
        Refusal(await HandleAsync(U1, ParsedRequest.Transfer("07805550120", 20_000, AmountUnit.Dinars)), ReasonCodes.AccountClosed);
    }

    [Fact]
    public async Task OwnNumberIsNotSupported()
    {
        await StartAsync();

        var refusal = Refusal(
            await HandleAsync(U1, ParsedRequest.Transfer(SeedFacts.DemoPhone, 20_000, AmountUnit.Dinars)), ReasonCodes.NotSupported);
        Assert.Equal(ReasonCodes.OwnNumberDetail, refusal.Detail);
    }

    [Fact]
    public async Task MissingAmountAsksTyped()
    {
        await StartAsync();

        var question = Question(
            await HandleAsync(U1, ParsedRequest.Transfer(HammoudiAlias, null, AmountUnit.None)), QuestionKinds.WhichAmount, QuestionAnswer.Typed);
        Assert.Empty(question.Options);
    }

    [Fact]
    public async Task MissingRecipientAsksTypedFirst()
    {
        await StartAsync();

        // Rule 16: who first, then how much.
        var question = Question(
            await HandleAsync(U1, ParsedRequest.Transfer(null, null, AmountUnit.None)), QuestionKinds.WhichContact, QuestionAnswer.Typed);
        Assert.Empty(question.Options);
    }

    [Fact]
    public async Task SmallNumberAsksReading()
    {
        await StartAsync();

        var question = Question(
            await HandleAsync(U1, ParsedRequest.Transfer(HammoudiAlias, 50, AmountUnit.None)), QuestionKinds.WhichAmount, QuestionAnswer.Tap);
        Assert.Equal([50_000L], question.Options.Select(o => o.AmountIqd!.Value));
    }

    [Fact]
    public async Task SmallNumberWithNoReadingAboveTheMinimumIsRefused()
    {
        await StartAsync();

        // 0.0005 thousand is not whole dinars and 0.0005 million is 500, below the minimum (Khalil, 14 Oct 2026).
        var refusal = Refusal(
            await HandleAsync(U1, ParsedRequest.Transfer(HammoudiAlias, 0.0005m, AmountUnit.None)), ReasonCodes.AmountBelowMinimum);
        Assert.Equal(1_000, refusal.AmountIqd);
    }

    [Fact]
    public void AnAliasWithNoContactIsOfferedByItsAccount()
    {
        var orphan = new ContactCandidate("alias", true, 42, "07805550199", null, "active", null, null, null);
        var saved = orphan with { IsAlias = false, ContactCode = "S01" };

        Assert.Equal("account:42", Orchestrator.ContactOptionValue(orphan));
        Assert.Equal("S01", Orchestrator.ContactOptionValue(saved));
    }

    [Fact]
    public async Task TheCardViewEqualsTheRecord()
    {
        await StartAsync();

        var issued = Assert.IsType<AgentReply.CardIssued>(await HandleAsync(U1, ParsedRequest.Transfer(HammoudiAlias, 50_000, AmountUnit.Dinars)));

        Assert.Equal(CardView.From(issued.Pending), issued.View);
        Assert.Equal(SeedFacts.DemoBalance - 51_000, issued.View.BalanceAfterIqd);
    }

    [Fact]
    public async Task OverCapsAreRefusedInOrder()
    {
        await StartAsync();
        var trader = SeedFacts.Contact("T01").SavedName;

        var perTransaction = Refusal(
            await HandleAsync(U3, ParsedRequest.Transfer(trader, 6_000_000, AmountUnit.Dinars)), ReasonCodes.OverPerTransactionCap);
        Assert.Equal(5_000_000, perTransaction.AmountIqd);

        var daily = Refusal(
            await HandleAsync(U3, ParsedRequest.Transfer(trader, 4_000_000, AmountUnit.Dinars)), ReasonCodes.OverDailyCap);
        Assert.Equal(3_000_000, daily.AmountIqd);
    }

    [Fact]
    public async Task InsufficientBalanceIsRefused()
    {
        await StartAsync();

        var refusal = Refusal(
            await HandleAsync(U2, ParsedRequest.Transfer(SeedFacts.Contact("Z01").Alias, 18_000, AmountUnit.Dinars)), ReasonCodes.InsufficientBalance);
        Assert.Equal(500, refusal.AmountIqd);
    }

    [Fact]
    public async Task BillCardComesFromTheBiller()
    {
        await StartAsync();

        var card = Card(await HandleAsync(U1, ParsedRequest.Bill("water", null, null, AmountUnit.None)));

        Assert.Equal("bill", card.Type);
        Assert.Equal(9_250, card.AmountIqd);
        Assert.Equal(500, card.FeeIqd);
        Assert.Equal(9_750, card.TotalIqd);
        Assert.Equal("BS3", card.BillCode);
        Assert.NotNull(card.BillAccountId);
        Assert.False(card.FirstTime);
    }

    [Fact]
    public async Task TwoElectricityAccountsAsk()
    {
        await StartAsync();

        var question = Question(
            await HandleAsync(U1, ParsedRequest.Bill("electricity", null, null, AmountUnit.None)), QuestionKinds.WhichBill, QuestionAnswer.Tap);
        Assert.Equal(["BS1", "BS2"], question.Options.Select(o => o.Value));
    }

    [Fact]
    public async Task PartOfABillIsRefused()
    {
        await StartAsync();

        var refusal = Refusal(
            await HandleAsync(U1, ParsedRequest.Bill("water", null, 5_000, AmountUnit.Dinars)), ReasonCodes.BillsPaidInFull);
        Assert.Equal(9_250, refusal.AmountIqd);
    }

    [Fact]
    public async Task NothingDueIsRefused()
    {
        await StartAsync();
        Refusal(await HandleAsync(U2, ParsedRequest.Bill("electricity", null, null, AmountUnit.None)), ReasonCodes.NothingDue);
    }

    [Fact]
    public async Task ATappedContactPicksOnlyAmongTheOfferedCandidates()
    {
        await StartAsync();
        var shared = ParsedRequest.Transfer(SeedFacts.Contact("S08").SavedName, 50_000, AmountUnit.Dinars);

        // A contact of the same user that is not among the candidates of this question picks nothing: it asks again.
        var again = Question(
            await HandleAsync(U1, shared, new SlotAnswers(ContactOption: "S01")), QuestionKinds.WhichContact, QuestionAnswer.Tap);
        Assert.Equal(["S08", "S09"], again.Options.Select(o => o.Value).Order(StringComparer.Ordinal));
        Assert.Equal(
            QuestionKinds.WhichContact,
            Question(await HandleAsync(U1, shared, new SlotAnswers(ContactOption: Orchestrator.AccountOptionPrefix + "1")), QuestionKinds.WhichContact, QuestionAnswer.Tap).Kind);

        // One of the candidates gives its card.
        var card = Card(await HandleAsync(U1, shared, new SlotAnswers(ContactOption: "S08")));
        Assert.Equal(SeedFacts.Contact("S08").Number, card.ToPhone);
    }

    [Fact]
    public async Task ATappedBillOrProductThatIsNotTheUsersIsResolvedAsIfUntapped()
    {
        await StartAsync();

        // BZ2 is U2's bill: for U1 it picks nothing, and the internet bill asks bill-or-store as without a tap.
        Question(
            await HandleAsync(U1, ParsedRequest.Bill("internet", null, null, AmountUnit.None), new SlotAnswers(BillCode: "BZ2")),
            QuestionKinds.BillOrStore,
            QuestionAnswer.Tap);
        Assert.Equal("BS4", Card(await HandleAsync(U1, ParsedRequest.Bill("internet", null, null, AmountUnit.None), new SlotAnswers(BillCode: "BS4"))).BillCode);

        Question(
            await HandleAsync(U1, ParsedRequest.Purchase(null, "internet", 40_000), new SlotAnswers(ProductCode: "NO-SUCH-1")),
            QuestionKinds.WhichOperator,
            QuestionAnswer.Tap);
        Assert.Equal("ZN-NET-40", Card(await HandleAsync(U1, ParsedRequest.Purchase(null, "internet", 40_000), new SlotAnswers(ProductCode: "ZN-NET-40"))).ProductCode);
    }

    [Fact]
    public async Task InternetWithABillAsksBillOrStore()
    {
        await StartAsync();

        var question = Question(
            await HandleAsync(U1, ParsedRequest.Bill("internet", null, null, AmountUnit.None)), QuestionKinds.BillOrStore, QuestionAnswer.Tap);
        Assert.Equal(["BS4", Option.StoreInternet], question.Options.Select(o => o.Value));
        Assert.Equal(35_000, question.Options[0].AmountIqd);
    }

    [Fact]
    public async Task PurchaseWithoutOperatorAsks()
    {
        await StartAsync();

        var question = Question(
            await HandleAsync(U1, ParsedRequest.Purchase(null, "internet", 40_000)), QuestionKinds.WhichOperator, QuestionAnswer.Tap);
        Assert.Equal(["zain"], question.Options.Select(o => o.Value));
    }

    [Fact]
    public async Task PurchaseCardUsesCataloguePrice()
    {
        await StartAsync();

        var card = Card(await HandleAsync(U1, ParsedRequest.Purchase("zain", "internet", 40_000)));

        Assert.Equal("purchase", card.Type);
        Assert.Equal("ZN-NET-40", card.ProductCode);
        Assert.Equal(40_000, card.AmountIqd);
        Assert.Equal(0, card.FeeIqd);
        Assert.Equal(40_000, card.TotalIqd);
        Assert.False(card.FirstTime);
    }

    [Fact]
    public async Task BalanceQuestionNeedsConsent()
    {
        await StartAsync();

        Assert.IsType<AgentReply.ConsentScreen>(await HandleAsync(U5, ParsedRequest.Balance()));
        Assert.Equal(new AgentReply.BalanceReply(SeedFacts.DemoBalance), await HandleAsync(U1, ParsedRequest.Balance()));

        var rows = await AuditAsync();
        Assert.Equal([AuditEvents.ConsentShown, AuditEvents.BalanceShown], rows.Select(r => r.Event));
        var shown = rows[1];
        Assert.Null(shown.AmountIqd);
        Assert.Null(shown.Reason);
        Assert.Null(shown.Reference);
    }

    [Fact]
    public async Task TransferNeedsConsent()
    {
        await StartAsync();

        Assert.IsType<AgentReply.ConsentScreen>(
            await HandleAsync(U5, ParsedRequest.Transfer(SeedFacts.Contact("N01").SavedName, 20_000, AmountUnit.Dinars)));
    }

    [Fact]
    public async Task UnsupportedUnclearAndCurrencyAreRefused()
    {
        await StartAsync();

        Assert.Equal(
            new AgentReply.Unsupported("withdrawal"),
            await HandleAsync(U1, new ParsedRequest { Intent = RequestIntent.Unsupported, UnsupportedReason = "withdrawal" }));
        Refusal(await HandleAsync(U1, new ParsedRequest { Intent = RequestIntent.Unclear }), ReasonCodes.NotUnderstood);
        Refusal(
            await HandleAsync(U1, ParsedRequest.Transfer(HammoudiAlias, 100, AmountUnit.None) with { CurrencyText = "USD" }),
            ReasonCodes.DinarsOnly);

        Assert.Equal(
            ["withdrawal", ReasonCodes.NotUnderstood, ReasonCodes.DinarsOnly],
            (await AuditAsync()).Select(r => r.Reason));
    }

    [Fact]
    public async Task AnotherPersonsBalanceRepliesOnlyOwnBalance()
    {
        await StartAsync();

        var reply = await HandleAsync(U1, new ParsedRequest { Intent = RequestIntent.Unsupported, UnsupportedReason = "other_balance" });

        Assert.Equal(new AgentReply.Refusal(ReasonCodes.OnlyOwnBalance, "other_balance", null), reply);
        var row = Assert.Single(await AuditAsync());
        Assert.Equal(AuditEvents.Refused, row.Event);
        Assert.Equal(ReasonCodes.OnlyOwnBalance, row.Reason);
        Assert.Null(row.PendingId);
    }

    [Fact]
    public async Task AnotherLanguageRepliesNotUnderstood()
    {
        await StartAsync();

        var reply = await HandleAsync(U1, new ParsedRequest { Intent = RequestIntent.Unsupported, UnsupportedReason = "other_language" });

        Assert.Equal(new AgentReply.Refusal(ReasonCodes.NotUnderstood, "other_language", null), reply);
        var row = Assert.Single(await AuditAsync());
        Assert.Equal(AuditEvents.Refused, row.Event);
        Assert.Equal(ReasonCodes.NotUnderstood, row.Reason);
    }

    [Fact]
    public async Task RepeatWarningWithinTenMinutes()
    {
        await StartAsync();
        var request = ParsedRequest.Transfer(HammoudiAlias, 50_000, AmountUnit.Dinars);

        var first = Card(await HandleAsync(U1, request));
        Assert.Null(first.Warning);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var outcome = await scope.ServiceProvider.GetRequiredService<ConfirmHandler>().ConfirmAsync(U1, first.Id, null, Token);
            Assert.IsType<ConfirmOutcome.Done>(outcome);
        }

        var again = Card(await HandleAsync(U1, request));

        Assert.Equal(CardBuilder.RepeatWarning, again.Warning);
        Assert.False(again.FirstTime);
        Assert.NotEqual(first.IdempotencyKey, again.IdempotencyKey);

        // A different amount to the same person carries no warning.
        Assert.Null(Card(await HandleAsync(U1, ParsedRequest.Transfer(HammoudiAlias, 20_000, AmountUnit.Dinars))).Warning);
    }
}
