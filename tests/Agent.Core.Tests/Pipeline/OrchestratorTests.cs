using System.Text.Json;
using Agent.Core.Aliases;
using Agent.Core.Audit;
using Agent.Core.Confirm;
using Agent.Core.Data;
using Agent.Core.Learning;
using Agent.Core.Pending;
using Agent.Core.Pipeline;
using Agent.Core.Requests;
using Agent.Core.Resolver;
using Agent.Core.Tests.Agent;
using Agent.Core.Tests.Resolver;
using Agent.Core.Tests.Wallet;
using Agent.Core.Wallet;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wallet.MockApi.Data;

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
    public async Task TheKillSwitchRefusesARequestThatWouldGetACard()
    {
        await StartAsync();
        var killSwitch = host.Services.GetRequiredService<KillSwitch>();
        killSwitch.Set(true);
        try
        {
            var refusal = Refusal(
                await HandleAsync(U1, ParsedRequest.Transfer(HammoudiAlias, 50_000, AmountUnit.Dinars)), ReasonCodes.NotDoneNothingTaken);
            Assert.Equal(ReasonCodes.KillSwitchDetail, refusal.Detail);

            // A bill and a purchase are stopped the same way, before any question.
            Assert.Equal(
                ReasonCodes.KillSwitchDetail,
                Refusal(await HandleAsync(U1, ParsedRequest.Bill("electricity", null, null, AmountUnit.None)), ReasonCodes.NotDoneNothingTaken).Detail);
            Assert.Equal(
                ReasonCodes.KillSwitchDetail,
                Refusal(await HandleAsync(U1, ParsedRequest.Purchase(null, null, null)), ReasonCodes.NotDoneNothingTaken).Detail);

            await using (var scope = host.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
                Assert.Equal(0, await db.PendingTransactions.CountAsync(Token));
            }

            var audit = await AuditAsync();
            Assert.Equal(3, audit.Count);
            Assert.All(audit, row =>
            {
                Assert.Equal(AuditEvents.Refused, row.Event);
                Assert.Equal(ReasonCodes.NotDoneNothingTaken, row.Reason);
                Assert.Equal(ReasonCodes.KillSwitchDetail, row.Detail);
            });

            // The balance question still answers: nothing moves.
            Assert.Equal(new AgentReply.BalanceReply(SeedFacts.DemoBalance), await HandleAsync(U1, ParsedRequest.Balance()));
        }
        finally
        {
            killSwitch.Set(false);
        }
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
    public async Task SmallNumberIsReadInThousands()
    {
        await StartAsync();

        // Rule 9 (Khalil, 16 Oct 2026): a bare 50 is 50,000, straight to the card; no reading question.
        var card = Card(await HandleAsync(U1, ParsedRequest.Transfer(HammoudiAlias, 50, AmountUnit.None)));
        Assert.Equal(50_000, card.AmountIqd);
    }

    [Fact]
    public async Task SmallNumberBelowTheMinimumIsRefusedAtTheChecks()
    {
        await StartAsync();

        // A bare 0.5 is 500, below the minimum of 1,000: the checks refuse it with the minimum in the reply.
        var refusal = Refusal(
            await HandleAsync(U1, ParsedRequest.Transfer(HammoudiAlias, 0.5m, AmountUnit.None)), ReasonCodes.AmountBelowMinimum);
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

    /// <summary>A request to pay part of a bill, as the parse call reads it (ruling 2 of milestone 8).</summary>
    private static ParsedRequest PartOf(string billerCode) => new()
    {
        Intent = RequestIntent.Unsupported,
        UnsupportedReason = LearningLogRule.PartOfBill,
        BillerCode = billerCode,
        AmountValue = 5,
        AmountUnit = AmountUnit.Thousand,
    };

    private static long AmountDueOf(string billCode)
        => SeedWallet.BillsOf(SeedFacts.Seed.Bills.Single(b => b.Id == billCode).User).Single(b => b.Code == billCode).AmountDueIqd;

    private async Task<int> PendingCountAsync()
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AgentDbContext>().PendingTransactions.CountAsync(Token);
    }

    [Fact]
    public async Task PartOfABillIsRefusedWithTheAmountDueAndNoCard()
    {
        await StartAsync();

        var refusal = Refusal(await HandleAsync(U1, PartOf("water")), ReasonCodes.BillsPaidInFull);

        Assert.Equal((LearningLogRule.PartOfBill, AmountDueOf("BS3")), (refusal.Detail, refusal.AmountIqd));
        Assert.Equal(0, await PendingCountAsync());
        var row = Assert.Single(await AuditAsync());
        Assert.Equal((AuditEvents.Refused, ReasonCodes.BillsPaidInFull), (row.Event, row.Reason));
    }

    [Fact]
    public async Task PartOfTheInternetBillIsTheBillNotTheStore()
    {
        await StartAsync();

        var refusal = Refusal(await HandleAsync(U1, PartOf("internet")), ReasonCodes.BillsPaidInFull);

        Assert.Equal((LearningLogRule.PartOfBill, AmountDueOf("BS4")), (refusal.Detail, refusal.AmountIqd));
        Assert.Equal(0, await PendingCountAsync());
    }

    [Fact]
    public async Task PartOfABillWithSeveralBillsAsksWhichBill()
    {
        await StartAsync();

        var question = Question(await HandleAsync(U1, PartOf("electricity")), QuestionKinds.WhichBill, QuestionAnswer.Tap);
        Assert.Equal(["BS1", "BS2"], question.Options.Select(o => o.Value));

        var refusal = Refusal(await HandleAsync(U1, PartOf("electricity"), new SlotAnswers(BillCode: "BS1")), ReasonCodes.BillsPaidInFull);
        Assert.Equal((LearningLogRule.PartOfBill, AmountDueOf("BS1")), (refusal.Detail, refusal.AmountIqd));
        Assert.Equal(0, await PendingCountAsync());
    }

    [Fact]
    public async Task PartOfABillWithNothingDueIsNothingDue()
    {
        await StartAsync();
        Refusal(await HandleAsync(U2, PartOf("electricity")), ReasonCodes.NothingDue);
        Assert.Equal(0, await PendingCountAsync());
    }

    [Fact]
    public async Task PartOfABillTheUserDoesNotHaveIsNotFound()
    {
        await StartAsync();
        const int u4 = 4;

        Refusal(await HandleAsync(u4, PartOf("water")), ReasonCodes.BillNotFound);
        // Rule 17's store branch is not a bill: part of it is no bill either (U2 has no internet bill).
        Refusal(await HandleAsync(U2, PartOf("internet")), ReasonCodes.BillNotFound);
        Assert.Equal(0, await PendingCountAsync());
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
    public async Task ATransferAnsweredByATapOnASharedWordQuestionCarriesTheAliasOffer()
    {
        // AMB-19: the recipient shares a name word with S08 and S09 (rule 15); the tap on S08 gives the card, and rule 18
        // offers what the user typed as an alias of S08's account.
        await StartAsync();
        var typed = DevSetText.SidecarRecipient("AMB-19")!;
        var request = ParsedRequest.Transfer(typed, 10, AmountUnit.Thousand);
        var question = Question(await HandleAsync(U1, request), QuestionKinds.WhichContact, QuestionAnswer.Tap);
        Assert.Contains("S08", question.Options.Select(o => o.Value));

        var issued = Assert.IsType<AgentReply.CardIssued>(await HandleAsync(U1, request, new SlotAnswers(ContactOption: "S08")));

        var offer = Assert.IsType<AliasOffer>(issued.Alias);
        Assert.Equal(typed.Trim(), offer.Text);
        Assert.Equal(issued.Pending.ToAccountId, offer.AccountId);
        Assert.Equal(SeedFacts.Contact("S08").Number, issued.Pending.ToPhone);
        Assert.Equal(SeedFacts.Account(SeedFacts.Contact("S08").Number).RegisteredName, offer.RecipientName);
    }

    [Fact]
    public async Task ATransferAnsweredByATapOnANearQuestionAboutSeveralAccountsCarriesNoOffer()
    {
        // S08 and S09 share one saved name of one word; with its last letter dropped the near step asks about both
        // (rule 6). The tap on S08 gives the card, and no offer is made: the save would refuse the one-word text as
        // shared (alias_shared, Khalil, 18 Oct 2026).
        await StartAsync();
        var typed = SeedFacts.Contact("S08").SavedName[..^1];
        var request = ParsedRequest.Transfer(typed, 10, AmountUnit.Thousand);
        var question = Question(await HandleAsync(U1, request), QuestionKinds.DidYouMean, QuestionAnswer.Tap);
        Assert.Equal(["S08", "S09"], question.Options.Select(o => o.Value).Order(StringComparer.Ordinal));

        var issued = Assert.IsType<AgentReply.CardIssued>(await HandleAsync(U1, request, new SlotAnswers(ContactOption: "S08")));

        Assert.Equal(SeedFacts.Contact("S08").Number, issued.Pending.ToPhone);
        Assert.Null(issued.Alias);
    }

    [Fact]
    public async Task ATransferAnsweredByATapOnAnExactNameSharedBySeveralCarriesNoOffer()
    {
        // AMB-02: a bare name saved for several contacts (rule 3); the tap pays, and no alias is offered.
        await StartAsync();
        var request = ParsedRequest.Transfer(DevSetText.SidecarRecipient("AMB-02"), 50, AmountUnit.Thousand);

        var issued = Assert.IsType<AgentReply.CardIssued>(await HandleAsync(U1, request, new SlotAnswers(ContactOption: "S10")));

        Assert.Equal(SeedFacts.Contact("S10").Number, issued.Pending.ToPhone);
        Assert.Null(issued.Alias);
    }

    [Fact]
    public async Task ATransferThatNeededNoQuestionCarriesNoOffer()
    {
        // CLN-01: the alias goes straight to the card.
        await StartAsync();
        var request = ParsedRequest.Transfer(DevSetText.SidecarRecipient("CLN-01"), 50, AmountUnit.Thousand);

        var issued = Assert.IsType<AgentReply.CardIssued>(await HandleAsync(U1, request));

        Assert.Null(issued.Alias);
    }

    [Fact]
    public async Task NoneOfTheseOnAContactQuestionIsRecipientNotFound()
    {
        await StartAsync();
        var shared = ParsedRequest.Transfer(SeedFacts.Contact("S08").SavedName, 50_000, AmountUnit.Dinars);

        var refusal = Refusal(await HandleAsync(U1, shared, new SlotAnswers(ContactOption: Option.NoneOfThese)), ReasonCodes.RecipientNotFound);

        Assert.Equal(shared.RecipientText, refusal.Detail);
        var row = Assert.Single(await AuditAsync());
        Assert.Equal((AuditEvents.Refused, ReasonCodes.RecipientNotFound, (string?)null), (row.Event, row.Reason, row.Detail));

        // On a name that resolves to one card, there is no question, so the answer is ignored.
        var exact = ParsedRequest.Transfer(HammoudiAlias, 50_000, AmountUnit.Dinars);
        Assert.Equal(SeedFacts.HammoudiPhone, Card(await HandleAsync(U1, exact, new SlotAnswers(ContactOption: Option.NoneOfThese))).ToPhone);
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
            QuestionKinds.WhichProduct,
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

        // AMB-23: rule 12 as decided, three fitting products across the operators are one which_product question.
        var question = Question(
            await HandleAsync(U1, ParsedRequest.Purchase(null, "credit", 5_000)), QuestionKinds.WhichProduct, QuestionAnswer.Tap);
        Assert.Equal(["ZN-CR-5", "AC-CR-5", "KR-CR-5"], question.Options.Select(o => o.Value));
    }

    [Fact]
    public async Task PurchaseWithNothingNamedAsksTheOperator()
    {
        await StartAsync();

        // AMB-24: all fifteen products fit, more than one question holds, so the operator is asked first.
        var question = Question(
            await HandleAsync(U1, ParsedRequest.Purchase(null, null, null)), QuestionKinds.WhichOperator, QuestionAnswer.Tap);
        Assert.Equal(["zain", "asiacell", "korek"], question.Options.Select(o => o.Value));
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
    public async Task AReceiveFromRequestIsUnsupportedAndGetsNoCard()
    {
        await StartAsync();

        // Asking to receive money from a contact (ADD-08) carries a recipient and an amount, and still never becomes a
        // transfer: no card, no pending record, one refused audit row.
        var request = new ParsedRequest
        {
            Intent = RequestIntent.Unsupported,
            UnsupportedReason = "receive_from",
            RecipientText = SeedFacts.Contact("S12").Alias,
            AmountValue = 100,
            AmountUnit = AmountUnit.Thousand,
        };

        Assert.Equal(new AgentReply.Unsupported("receive_from"), await HandleAsync(U1, request));
        Assert.Equal(0, await PendingCountAsync());
        var row = Assert.Single(await AuditAsync());
        Assert.Equal((AuditEvents.Refused, "receive_from"), (row.Event, row.Reason));
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
    public async Task AnotherCurrencyParsedAsUnsupportedRepliesDinarsOnly()
    {
        await StartAsync();
        var request = new ParsedRequest
        {
            Intent = RequestIntent.Unsupported,
            UnsupportedReason = "other_currency",
            RecipientText = HammoudiAlias,
            AmountValue = 100,
            CurrencyText = "dollars",
        };

        var reply = await HandleAsync(U1, request);

        Assert.Equal(new AgentReply.Refusal(ReasonCodes.DinarsOnly, "other_currency", null), reply);
        var row = Assert.Single(await AuditAsync());
        Assert.Equal((AuditEvents.Refused, ReasonCodes.DinarsOnly), (row.Event, row.Reason));
        Assert.Null(row.PendingId);
        Assert.Equal(
            new global::Agent.Core.Learning.LearningEntry("other_currency", null),
            global::Agent.Core.Learning.LearningLogRule.EntryFor(reply, request));
    }

    [Fact]
    public async Task AnotherLanguageRepliesUnsupportedWithItsOwnReason()
    {
        await StartAsync();

        // GAP-08 and ADD-23: other_language takes the general unsupported path, so the phone shows its own string
        // (Khalil, 17 Oct 2026).
        var reply = await HandleAsync(U1, new ParsedRequest { Intent = RequestIntent.Unsupported, UnsupportedReason = "other_language" });

        Assert.Equal(new AgentReply.Unsupported("other_language"), reply);
        var row = Assert.Single(await AuditAsync());
        Assert.Equal((AuditEvents.Refused, "other_language"), (row.Event, row.Reason));
    }

    private static ParsedRequest ChooseAccount(string? recipient, decimal? amount) => new()
    {
        Intent = RequestIntent.Unsupported,
        UnsupportedReason = "choose_account",
        RecipientText = recipient,
        AmountValue = amount,
        AmountUnit = AmountUnit.Thousand,
    };

    [Fact]
    public async Task ChooseAccountWithARecipientAndAnAmountGivesTheCardWithTheNote()
    {
        await StartAsync();
        var request = ChooseAccount(HammoudiAlias, 30);

        var reply = Assert.IsType<AgentReply.CardIssued>(await HandleAsync(U1, request));

        Assert.Equal(ReasonCodes.AccountChoiceUnavailable, reply.Note);
        Assert.Equal(("transfer", 30_000L, SeedFacts.HammoudiPhone), (reply.Pending.Type, reply.Pending.AmountIqd, reply.Pending.ToPhone));
        Assert.Equal(
            new global::Agent.Core.Learning.LearningEntry("choose_account", null),
            global::Agent.Core.Learning.LearningLogRule.EntryFor(reply, request));
        Assert.Equal([AuditEvents.Created], (await AuditAsync()).Select(r => r.Event));
    }

    [Fact]
    public async Task ChooseAccountWithoutARecipientOrAnAmountIsUnsupported()
    {
        await StartAsync();

        Assert.Equal(new AgentReply.Unsupported("choose_account"), await HandleAsync(U1, ChooseAccount(null, 30)));
        Assert.Equal(new AgentReply.Unsupported("choose_account"), await HandleAsync(U1, ChooseAccount(HammoudiAlias, null)));
    }

    [Fact]
    public async Task ChooseAccountPassesAQuestionThroughWithoutTheNote()
    {
        await StartAsync();

        // A shared name asks which contact; the tapped answer then gives the card with the note.
        var request = ChooseAccount(SeedFacts.Contact("S08").SavedName, 30);
        Question(await HandleAsync(U1, request), QuestionKinds.WhichContact, QuestionAnswer.Tap);

        var card = Assert.IsType<AgentReply.CardIssued>(await HandleAsync(U1, request, new SlotAnswers(ContactOption: "S09")));
        Assert.Equal(ReasonCodes.AccountChoiceUnavailable, card.Note);
        Assert.Equal(SeedFacts.Contact("S09").Number, card.Pending.ToPhone);
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

    private async Task<AgentReply> ReissueAsync(int userId, PendingRecord previous, ReissueKind kind)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<Orchestrator>().ReissueAsync(userId, previous, kind, Token);
    }

    /// <summary>Confirms <paramref name="card"/> through the Confirm handler and returns its record as stored after.</summary>
    private async Task<PendingRecord> PayAsync(PendingRecord card)
    {
        await using var scope = host.Services.CreateAsyncScope();
        Assert.IsType<ConfirmOutcome.Done>(
            await scope.ServiceProvider.GetRequiredService<ConfirmHandler>().ConfirmAsync(card.UserId, card.Id, null, Token));
        var stored = await scope.ServiceProvider.GetRequiredService<PendingStore>().FindAsync(card.Id, card.UserId, Token);
        return Assert.IsType<PendingRecord>(stored);
    }

    [Fact]
    public async Task ReissueOfAPaidBillIsNothingDue()
    {
        await StartAsync();
        var paid = await PayAsync(Card(await HandleAsync(U1, ParsedRequest.Bill("water", null, null, AmountUnit.None))));
        var before = (await AuditAsync()).Count;

        // D19 re-reads the bill: once paid, its amount due is 0, so there is no second card.
        Refusal(await ReissueAsync(U1, paid, ReissueKind.SendAgain), ReasonCodes.NothingDue);

        Assert.DoesNotContain((await AuditAsync()).Skip(before), a => a.Event == AuditEvents.Created);
    }

    [Fact]
    public async Task ReissueOfAnUnpaidBillTakesTheCurrentAmountDue()
    {
        await StartAsync();
        var first = Card(await HandleAsync(U1, ParsedRequest.Bill("water", null, null, AmountUnit.None)));

        var again = Card(await ReissueAsync(U1, first, ReissueKind.TryAgain));

        Assert.NotEqual(first.Id, again.Id);
        Assert.NotEqual(first.IdempotencyKey, again.IdempotencyKey);
        Assert.Equal((first.Type, first.BillCode, first.BillAccountId, first.DisplayName), (again.Type, again.BillCode, again.BillAccountId, again.DisplayName));
        Assert.Equal((9_250L, 9_750L), (again.AmountIqd, again.TotalIqd));
        Assert.Null(again.Warning);
    }

    [Fact]
    public async Task ReissueOfAPurchaseTakesTheCataloguePrice()
    {
        await StartAsync();
        var first = Card(await HandleAsync(U1, ParsedRequest.Purchase("zain", "internet", 40_000)));

        var again = Card(await ReissueAsync(U1, first, ReissueKind.SendAgain));

        Assert.Equal((first.ProductId, first.ProductCode, first.AmountIqd), (again.ProductId, again.ProductCode, again.AmountIqd));
        Assert.Equal(CardBuilder.RepeatWarning, again.Warning);
    }

    [Fact]
    public async Task ReissueOfARecordOfAnotherUserThrows()
    {
        await StartAsync();
        var card = Card(await HandleAsync(U1, ParsedRequest.Transfer(HammoudiAlias, 50_000, AmountUnit.Dinars)));

        await Assert.ThrowsAsync<ArgumentException>(() => ReissueAsync(U2, card, ReissueKind.TryAgain));
    }

    [Fact]
    public async Task TheKillSwitchRefusesAReissue()
    {
        await StartAsync();
        var card = Card(await HandleAsync(U1, ParsedRequest.Transfer(HammoudiAlias, 50_000, AmountUnit.Dinars)));
        var killSwitch = host.Services.GetRequiredService<KillSwitch>();
        killSwitch.Set(true);
        try
        {
            var refusal = Refusal(await ReissueAsync(U1, card, ReissueKind.TryAgain), ReasonCodes.NotDoneNothingTaken);
            Assert.Equal(ReasonCodes.KillSwitchDetail, refusal.Detail);
            Assert.Single(await AuditAsync(), a => a.Event == AuditEvents.Created);
        }
        finally
        {
            killSwitch.Set(false);
        }
    }

    /// <summary>An orchestrator (with its checks and card builder) that reaches the wallet through <paramref name="client"/>.</summary>
    private static Orchestrator OrchestratorThrough(IServiceProvider services, WalletClient client)
    {
        var checks = ActivatorUtilities.CreateInstance<Checks>(services, client);
        var cards = ActivatorUtilities.CreateInstance<CardBuilder>(services, client);
        return ActivatorUtilities.CreateInstance<Orchestrator>(services, client, checks, cards);
    }

    /// <summary>
    /// Security review I1: the switch is turned on while the checks wait for the wallet's quote, after the first check
    /// passed. The second check, just before the record opens, refuses: no card.
    /// </summary>
    [Fact]
    public async Task TheKillSwitchTurnedOnDuringTheChecksIssuesNoCard()
    {
        await StartAsync();
        var killSwitch = host.Services.GetRequiredService<KillSwitch>();
        var flipping = new RewritingHandler(path => path == "/quotes" ? path : null, () => killSwitch.Set(true))
        {
            InnerHandler = host.Wallet.CreateHandler(),
        };
        try
        {
            await using var scope = host.Services.CreateAsyncScope();
            var orchestrator = OrchestratorThrough(scope.ServiceProvider, host.CreateClient(flipping, TimeSpan.FromSeconds(10)));

            var reply = await orchestrator.HandleAsync(U1, ParsedRequest.Transfer(HammoudiAlias, 50_000, AmountUnit.Dinars), Token);

            Assert.True(flipping.Matched);
            Assert.Equal(ReasonCodes.KillSwitchDetail, Refusal(reply, ReasonCodes.NotDoneNothingTaken).Detail);
            Assert.DoesNotContain(await AuditAsync(), a => a.Event == AuditEvents.Created);
        }
        finally
        {
            killSwitch.Set(false);
        }
    }

    /// <summary>
    /// Security review I2: between the first card and the reissue the phone was given to another account (here X01's
    /// account takes Hammoudi's number in the wallet). The new card carries that account's ID and registered name, so the
    /// name shown is the account paid; the stored phone and display name stay.
    /// </summary>
    [Fact]
    public async Task AReissuedTransferCarriesTheAccountReadFresh()
    {
        await StartAsync();
        var first = Card(await HandleAsync(U1, ParsedRequest.Transfer(HammoudiAlias, 50_000, AmountUnit.Dinars)));
        await using (var wallet = host.Wallet.CreateScope())
        {
            var db = wallet.ServiceProvider.GetRequiredService<WalletDbContext>();
            Assert.Equal(1, await db.Accounts.Where(a => a.Phone == SeedFacts.HammoudiPhone)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.Phone, "07805559998"), Token));
            Assert.Equal(1, await db.Accounts.Where(a => a.Phone == NewPhone)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.Phone, SeedFacts.HammoudiPhone), Token));
        }

        await using var scope = host.Services.CreateAsyncScope();
        var read = await scope.ServiceProvider.GetRequiredService<WalletClient>().GetAccountByPhoneAsync(SeedFacts.HammoudiPhone, Token);
        var now = Assert.IsType<WalletResult<WalletAccount>.Ok>(read).Value;
        Assert.NotEqual(first.ToAccountId, now.AccountId);

        var again = Card(await scope.ServiceProvider.GetRequiredService<Orchestrator>().ReissueAsync(U1, first, ReissueKind.TryAgain, Token));

        Assert.Equal(now.AccountId, again.ToAccountId);
        Assert.Equal(SeedFacts.Account(NewPhone).RegisteredName, again.RegisteredName);
        Assert.NotEqual(first.RegisteredName, again.RegisteredName);
        Assert.Equal((first.ToPhone, first.DisplayName), (again.ToPhone, again.DisplayName));
    }

    /// <summary>
    /// For a request whose path <paramref name="rewrite"/> maps to a path (not null): runs <paramref name="onMatch"/>,
    /// sends it to that path instead, and records the match. Every other request passes on as it is.
    /// </summary>
    private sealed class RewritingHandler(Func<string, string?> rewrite, Action onMatch) : DelegatingHandler
    {
        public bool Matched { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is { } uri && rewrite(uri.AbsolutePath) is { } path)
            {
                Matched = true;
                onMatch();
                request.RequestUri = new UriBuilder(uri) { Path = path }.Uri;
            }

            return base.SendAsync(request, cancellationToken);
        }
    }
}
