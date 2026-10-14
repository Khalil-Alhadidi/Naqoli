using Agent.Core.Pipeline;
using Agent.Core.Tests.Agent;
using Agent.Core.Tests.Wallet;
using Agent.Core.Wallet;
using Microsoft.Extensions.DependencyInjection;

namespace Agent.Core.Tests.Pipeline;

/// <summary>
/// The ordered checks (format, recipient, per-transaction cap, daily cap, balance on the total) against the agent's
/// in-process wallet. Each order case fails more than one check, so only the first one may be the reason.
/// </summary>
[Collection(AgentCollection.Name)]
public class ChecksTests(AgentHost host)
{
    /// <summary>S20: U1's contact whose account is closed.</summary>
    private const string ClosedPhone = "07805550120";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string ContactPhone(string code) => SeedFacts.Contact(code).Number;

    private async Task<CheckResult> RunAsync(CheckInput input)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<Checks>().RunAsync(input, Token);
    }

    private async Task<WalletAccount> AccountAsync(string phone)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var wallet = scope.ServiceProvider.GetRequiredService<WalletClient>();
        return Assert.IsType<WalletResult<WalletAccount>.Ok>(await wallet.GetAccountByPhoneAsync(phone, Token)).Value;
    }

    private async Task<CheckInput> TransferAsync(int userId, long amount, string toPhone)
    {
        var account = await AccountAsync(toPhone);
        return new CheckInput(userId, "transfer", amount, toPhone, account.AccountId, account.Status, null, null, SkipFormat: false);
    }

    private async Task<CheckInput> BillAsync(int userId, string billCode, long? amount = null, int? ofUserId = null)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var wallet = scope.ServiceProvider.GetRequiredService<WalletClient>();
        var bills = Assert.IsType<WalletResult<IReadOnlyList<WalletBill>>.Ok>(await wallet.GetBillsAsync(ofUserId ?? userId, Token)).Value;
        var bill = bills.Single(b => b.Code == billCode);
        return new CheckInput(userId, "bill", amount ?? bill.AmountDueIqd, null, null, null, bill.BillAccountId, null, SkipFormat: true);
    }

    private async Task StartAsync()
    {
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);
        await host.ResetAsync();
    }

    [Fact]
    public async Task ClosedAccountComesBeforeThePerTransactionCap()
    {
        await StartAsync();

        // 6,000,000 is over the cap and over U1's balance too; the closed recipient is checked first.
        var input = await TransferAsync(SeedFacts.DemoUserId, 6_000_000, ClosedPhone);
        Assert.Equal("closed", input.AccountStatus);

        Assert.Equal(new CheckResult.Fail(ReasonCodes.AccountClosed, null, null), await RunAsync(input));
    }

    [Fact]
    public async Task PerTransactionCapComesBeforeTheBalance()
    {
        await StartAsync();

        var input = await TransferAsync(SeedFacts.TightUserId, 6_000_000, ContactPhone("Z01"));

        Assert.Equal(new CheckResult.Fail(ReasonCodes.OverPerTransactionCap, null, 5_000_000), await RunAsync(input));
    }

    [Fact]
    public async Task TraderOverTheDailyCap()
    {
        await StartAsync();

        // U3 sent 7,000,000 today; 4,000,000 more is under the per-transaction cap and the balance, but over the day.
        var input = await TransferAsync(SeedFacts.TraderUserId, 4_000_000, ContactPhone("T01"));

        Assert.Equal(new CheckResult.Fail(ReasonCodes.OverDailyCap, null, 3_000_000), await RunAsync(input));
    }

    [Fact]
    public async Task TraderUnderTheDailyCapPassesWithWhatWasSpentToday()
    {
        await StartAsync();

        var input = await TransferAsync(SeedFacts.TraderUserId, 3_000_000, ContactPhone("T01"));

        var pass = Assert.IsType<CheckResult.Pass>(await RunAsync(input));
        Assert.Equal(7_000_000, pass.DailySpentIqd);
        Assert.Equal(3_001_000, pass.Quote.TotalIqd);
        Assert.True(pass.Quote.PinRequired);
    }

    [Fact]
    public async Task TightUserShortByFiveHundred()
    {
        await StartAsync();

        // 18,000 + 1,000 fee = 19,000 against a balance of 18,500.
        var input = await TransferAsync(SeedFacts.TightUserId, 18_000, ContactPhone("Z01"));

        Assert.Equal(new CheckResult.Fail(ReasonCodes.InsufficientBalance, null, 500), await RunAsync(input));
    }

    [Fact]
    public async Task AmountBelowTheMinimumComesFirst()
    {
        await StartAsync();

        Assert.Equal(
            new CheckResult.Fail(ReasonCodes.AmountBelowMinimum, null, 1_000),
            await RunAsync(await TransferAsync(SeedFacts.DemoUserId, 500, SeedFacts.HammoudiPhone)));

        // Below the minimum and to a closed account: the format check is first.
        Assert.Equal(
            new CheckResult.Fail(ReasonCodes.AmountBelowMinimum, null, 1_000),
            await RunAsync(await TransferAsync(SeedFacts.DemoUserId, 500, ClosedPhone)));
    }

    [Fact]
    public async Task TransferToHammoudiPasses()
    {
        await StartAsync();

        var pass = Assert.IsType<CheckResult.Pass>(await RunAsync(await TransferAsync(SeedFacts.DemoUserId, 50_000, SeedFacts.HammoudiPhone)));

        Assert.Equal(0, pass.DailySpentIqd);
        Assert.Equal(1_000, pass.Quote.FeeIqd);
        Assert.Equal(51_000, pass.Quote.TotalIqd);
        Assert.Equal(699_000, pass.Quote.BalanceAfterIqd);
    }

    [Fact]
    public async Task BillAmountSkipsTheFormatCheckAndPasses()
    {
        await StartAsync();

        var pass = Assert.IsType<CheckResult.Pass>(await RunAsync(await BillAsync(SeedFacts.DemoUserId, "BS3")));

        Assert.Equal(9_250, pass.Quote.AmountIqd);
        Assert.Equal(500, pass.Quote.FeeIqd);
    }

    [Fact]
    public async Task TransferToOwnNumberIsNotSupported()
    {
        await StartAsync();

        var input = await TransferAsync(SeedFacts.DemoUserId, 50_000, SeedFacts.DemoPhone);

        Assert.Equal(new CheckResult.Fail(ReasonCodes.NotSupported, ReasonCodes.OwnNumberDetail, null), await RunAsync(input));
    }

    [Fact]
    public async Task BillWithNothingDueIsNothingDue()
    {
        await StartAsync();

        var input = await BillAsync(SeedFacts.TightUserId, "BZ1");

        Assert.Equal(new CheckResult.Fail(ReasonCodes.NothingDue, null, null), await RunAsync(input));
    }

    [Fact]
    public async Task PartOfABillIsPaidInFullWithTheAmountDue()
    {
        await StartAsync();

        var input = await BillAsync(SeedFacts.DemoUserId, "BS3", amount: 5_000);

        Assert.Equal(new CheckResult.Fail(ReasonCodes.BillsPaidInFull, null, 9_250), await RunAsync(input));
    }

    [Fact]
    public async Task AnotherUsersBillIsRecipientNotFound()
    {
        await StartAsync();

        var input = await BillAsync(SeedFacts.DemoUserId, "BZ2", ofUserId: SeedFacts.TightUserId);

        Assert.Equal(new CheckResult.Fail(ReasonCodes.RecipientNotFound, null, null), await RunAsync(input));
    }

    [Fact]
    public async Task OtherQuoteRefusalIsNotDoneWithTheWalletCode()
    {
        await StartAsync();

        var input = new CheckInput(SeedFacts.DemoUserId, "purchase", 5_000, null, null, null, null, ProductId: 9_999, SkipFormat: true);

        Assert.Equal(new CheckResult.Fail(ReasonCodes.NotDoneNothingTaken, "unknown_product", null), await RunAsync(input));
    }
}
