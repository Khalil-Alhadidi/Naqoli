using Agent.Core.Confirm;
using Agent.Core.Data;
using Agent.Core.Learning;
using Agent.Core.Pipeline;
using Agent.Core.Requests;
using Agent.Core.Tests.Agent;
using Agent.Core.Tests.Conversation;
using Agent.Core.Tests.Wallet;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Agent.Core.Tests.Learning;

/// <summary>
/// The learning-log table (D12, slide 2): what a person reviews is the masked text, never the typed number and never a
/// PIN; the conversation writes one row for every reply that <see cref="LearningLogRule"/> logs.
/// </summary>
[Collection(AgentCollection.Name)]
public class LearningLogTests(AgentHost host)
{
    private const int U3 = SeedFacts.TraderUserId;

    private const string ZorroText = "pay Zorro 50000, his number is 07701234567";

    private const string ZorroMasked = "pay Zorro 50000, his number is <NUM_1>";

    private const string TraderText = "pay the trader";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string TraderPin => SeedFacts.PinOf("U3");

    /// <summary>A well-formed PIN that is not U3's.</summary>
    private static string WrongPin => TraderPin == "7531" ? "7532" : "7531";

    private async Task<List<LearningEntryRow>> RowsAsync()
    {
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
        return await db.LearningEntries.AsNoTracking().OrderBy(r => r.Id).ToListAsync(Token);
    }

    private async Task WriteAsync(LearningEntry entry, string maskedText)
    {
        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<LearningLog>().WriteAsync(SeedFacts.DemoUserId, entry, maskedText, Token);
    }

    [Fact]
    public async Task WriteStoresTheEntryTheMaskedTextAndTheTime()
    {
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);
        await host.ResetAsync();

        await WriteAsync(new LearningEntry(LearningLogRule.RecipientNotFound, "Zorro"), ZorroMasked);

        var row = Assert.Single(await RowsAsync());
        Assert.Equal(
            (SeedFacts.DemoUserId, LearningLogRule.RecipientNotFound, "Zorro", ZorroMasked, false, host.Clock.GetUtcNow()),
            (row.UserId, row.Reason, row.Detail, row.MaskedText, row.Reviewed, row.At));
    }

    [Fact]
    public async Task ANumberThatReachesTheLogIsMaskedAndLongTextIsCut()
    {
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);
        await host.ResetAsync();

        // A typed answer to a question goes to the orchestrator as typed, so a refusal's detail can hold a number.
        await WriteAsync(new LearningEntry(LearningLogRule.RecipientNotFound, "Zorro 0770 123 4567"), ZorroText);
        await WriteAsync(new LearningEntry(LearningLogRule.NotUnderstood, new string('d', 300)), new string('t', 3_000));

        var rows = await RowsAsync();
        Assert.Equal(("Zorro <NUM_1>", ZorroMasked), (rows[0].Detail, rows[0].MaskedText));
        Assert.Equal(AgentDbContext.TextLength, rows[1].Detail?.Length);
        Assert.Equal(AgentDbContext.LearningTextLength, rows[1].MaskedText.Length);
    }

    [Fact]
    public void WriteHasNoParameterForAPinABalanceOrAPurchaseCode()
    {
        var names = typeof(LearningLog).GetMethod(nameof(LearningLog.WriteAsync))!
            .GetParameters()
            .Select(p => p.Name!.ToLowerInvariant());

        Assert.DoesNotContain(names, n => n.Contains("pin") || n.Contains("balance") || n.Contains("code"));
    }

    [Fact]
    public async Task ARecipientNotFoundReplyWritesOneRowWithMaskedTextAndNoPin()
    {
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);
        var parser = new CapturingParser(new Dictionary<string, ParsedRequest>
        {
            [ZorroMasked] = ParsedRequest.Transfer("Zorro", 50_000, AmountUnit.Dinars),
            [TraderText] = ParsedRequest.Transfer(SeedFacts.Contact("T01").SavedName, 1_100_000, AmountUnit.Dinars),
        });
        await using var session = await ConversationHarness.StartAsync(host, parser, U3);
        var conversation = session.Conversation;

        await conversation.SendAsync(ZorroText);

        Assert.Equal(ReasonCodes.RecipientNotFound, Assert.IsType<AgentReply.Refusal>(conversation.LastReply).Code);
        var row = Assert.Single(await RowsAsync());
        Assert.Equal(
            (U3, LearningLogRule.RecipientNotFound, "Zorro", ZorroMasked),
            (row.UserId, row.Reason, row.Detail, row.MaskedText));

        // The same session pays with a PIN, once wrong and once right: a card and its outcomes log nothing.
        await conversation.SendAsync(TraderText);
        Assert.True(conversation.PinVisible);
        Assert.IsType<ConfirmOutcome.PinWrong>(await conversation.ConfirmShownAsync(WrongPin));
        Assert.IsType<ConfirmOutcome.Done>(await conversation.ConfirmShownAsync(TraderPin));

        var rows = await RowsAsync();
        Assert.Single(rows);
        Assert.All(rows, r =>
        {
            var stored = string.Join('|', r.Reason, r.Detail, r.MaskedText);
            Assert.DoesNotContain(TraderPin, stored, StringComparison.Ordinal);
            Assert.DoesNotContain(WrongPin, stored, StringComparison.Ordinal);
            Assert.DoesNotContain("07701234567", stored, StringComparison.Ordinal);
        });
    }
}
