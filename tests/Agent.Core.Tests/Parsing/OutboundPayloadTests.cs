using System.Text.Json;
using Agent.Core.Aliases;
using Agent.Core.Confirm;
using Agent.Core.Parsing;
using Agent.Core.Pipeline;
using Agent.Core.Store;
using Agent.Core.Tests.Agent;
using Agent.Core.Tests.Conversation;
using Agent.Core.Tests.Wallet;
using Agent.Core.Text;
using Agent.Core.Wallet;
using Agent.Eval.Checks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Wallet.MockApi.Data;

namespace Agent.Core.Tests.Parsing;

/// <summary>
/// Rule 5, end to end (the milestone's "Done when"): the full conversation, over the Claude parser and a fake chat
/// client that captures every message and option handed to it, plays a transfer, a transfer to a typed phone number, a
/// bill, a purchase and the balance question while cards are pending. Nothing the model receives holds a seed phone or
/// subscriber number, a balance figure, a saved name, an alias, a registered name or a PIN; the masked token and the
/// pending note are there. Seed text is read from the loaded seed, never written here.
/// </summary>
[Collection(AgentCollection.Name)]
public sealed class OutboundPayloadTests(AgentHost host)
{
    private const string HammoudiSentence = "send fifty thousand to my brother Hammoudi";

    /// <summary>A transfer to the alias <see cref="ASavedAliasNeverReachesTheModel"/> saves first.</summary>
    private const string AliasSentence = "send ten thousand to my usual one";

    /// <summary>The alias that test saves: S03's saved name and one Latin letter, on S03's account.</summary>
    private static string SavedAliasText => SeedFacts.Contact("S03").SavedName + " x";

    private static string Reply(string fields) => "{\"requests\":[{" + fields + "}]}";

    private static string Nulls(params string[] except)
    {
        string[] all =
        [
            "recipient_text", "amount_value", "biller_code", "bill_label_text", "operator_text", "product_type", "product_value",
            "unsupported_reason", "currency_text",
        ];
        return string.Join(',', all.Where(f => !except.Contains(f)).Select(f => "\"" + f + "\":null"));
    }

    /// <summary>The model's answer to each message (the fake stands in for the model, so it may name the alias).</summary>
    private static string Answer(string message)
    {
        if (message.Contains(HammoudiSentence, StringComparison.Ordinal))
        {
            var alias = JsonSerializer.Serialize(SeedFacts.Contact("S01").Alias);
            return Reply("\"intent\":\"transfer\",\"recipient_text\":" + alias + ",\"amount_value\":50,\"amount_unit\":\"thousand\"," + Nulls("recipient_text", "amount_value"));
        }

        if (message.Contains(AliasSentence, StringComparison.Ordinal))
        {
            var alias = JsonSerializer.Serialize(SavedAliasText);
            return Reply("\"intent\":\"transfer\",\"recipient_text\":" + alias + ",\"amount_value\":10,\"amount_unit\":\"thousand\"," + Nulls("recipient_text", "amount_value"));
        }

        if (message.Contains("<NUM_1>", StringComparison.Ordinal))
            return Reply("\"intent\":\"transfer\",\"recipient_text\":\"<NUM_1>\",\"amount_value\":25000,\"amount_unit\":\"none\"," + Nulls("recipient_text", "amount_value"));
        if (message.Contains("water", StringComparison.Ordinal))
            return Reply("\"intent\":\"bill\",\"amount_unit\":\"none\",\"biller_code\":\"water\"," + Nulls("biller_code"));
        if (message.Contains("credit", StringComparison.Ordinal))
            return Reply("\"intent\":\"purchase\",\"amount_unit\":\"none\",\"operator_text\":\"zain\",\"product_type\":\"credit\",\"product_value\":5000," + Nulls("operator_text", "product_type", "product_value"));
        return Reply("\"intent\":\"balance\",\"amount_unit\":\"none\"," + Nulls());
    }

    [Fact]
    public Task NothingFromTheWalletReachesTheModel() => PlayAndCheckAsync(saveAlias: false);

    /// <summary>
    /// Milestone 9a: with an alias saved in the wallet before the sentences, and one more transfer to it at the end, the
    /// alias text never reaches the model, nor does anything else the existing test checks.
    /// </summary>
    [Fact]
    public Task ASavedAliasNeverReachesTheModel() => PlayAndCheckAsync(saveAlias: true);

    /// <summary>
    /// Milestone 9b: with the purchase confirmed and My orders read before the balance question, neither the new purchase
    /// code, nor any order code the wallet returns, nor any seeded purchase code reaches the model, in either digit form.
    /// </summary>
    [Fact]
    public Task APurchaseCodeNeverReachesTheModel() => PlayAndCheckAsync(saveAlias: false, confirmPurchase: true);

    private async Task PlayAndCheckAsync(bool saveAlias, bool confirmPurchase = false)
    {
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);
        var client = new FakeChatClient
        {
            Reply = (messages, _, _) => Task.FromResult(FakeChatClient.Json(Answer(messages[^1].Text), 1_000, 100)),
        };
        var trace = new ClaudeParserTests.RecordingTrace();
        var parser = new ClaudeParser(
            client, Microsoft.Extensions.Options.Options.Create(ClaudeParserTests.Options()), TimeProvider.System, trace, NullLogger<ClaudeParser>.Instance);
        await using var session = await ConversationHarness.StartAsync(host, parser, SeedFacts.DemoUserId);
        var conversation = session.Conversation;
        if (saveAlias)
        {
            var contacts = Assert.IsType<WalletResult<WalletContacts>.Ok>(
                await conversation.ListAliasesAsync()).Value;
            var s03 = Assert.Single(contacts.Contacts, c => c.Code == "S03");
            Assert.IsType<AliasResult.Saved>(await conversation.AddAliasAsync(SavedAliasText, s03.TargetAccountId));
        }

        await conversation.SendAsync(HammoudiSentence);
        var hammoudiCard = Assert.IsType<CardView>(conversation.Card);
        await conversation.SendAsync("and send 25000 to " + SeedFacts.HammoudiPhone);
        var numberCard = Assert.IsType<CardView>(conversation.Card);
        Assert.Equal(SeedFacts.HammoudiPhone, numberCard.ToPhone);
        await conversation.SendAsync("pay the water bill");
        var billCard = Assert.IsType<CardView>(conversation.Card);
        await conversation.SendAsync("zain credit five");
        var purchaseCard = Assert.IsType<CardView>(conversation.Card);
        List<string> purchaseCodes = [];
        if (confirmPurchase)
        {
            var bought = Assert.IsType<ConfirmOutcome.Done>(await conversation.ConfirmAsync(null));
            Assert.NotNull(bought.PurchaseCode);
            var orders = Assert.IsType<WalletResult<IReadOnlyList<PurchaseOrder>>.Ok>(await conversation.ListOrdersAsync()).Value;
            Assert.Equal(bought.PurchaseCode, orders[0].Code);
            purchaseCodes = [bought.PurchaseCode, .. orders.Select(o => o.Code)];
        }

        var callsBeforeBalance = client.Calls.Count;
        await conversation.SendAsync("how much do I have");
        Assert.IsType<AgentReply.BalanceReply>(conversation.LastReply);
        if (saveAlias)
        {
            // The alias gives its card straight away: an exact hit, no question.
            await conversation.SendAsync(AliasSentence);
            var aliasCard = Assert.IsType<CardView>(conversation.Card);
            Assert.Equal(SavedAliasText, aliasCard.DisplayName);
            Assert.Null(conversation.Question);
        }

        Assert.Equal(saveAlias ? 6 : 5, client.Calls.Count);
        var texts = client.Calls
            .SelectMany(c => c.Messages.Select(m => m.Text)
                .Append(c.Options?.ResponseFormat is ChatResponseFormatJson { Schema: { } schema } ? schema.GetRawText() : string.Empty))
            .ToList();
        var payload = string.Join('\n', texts);

        // Present: the masked number and the pending note (this test fails if masking or the note is removed).
        Assert.Contains("<NUM_1>", payload, StringComparison.Ordinal);
        Assert.Contains("Pending: pending_card transfer", payload, StringComparison.Ordinal);

        // Absent: wallet numbers, subscriber numbers and the users' balances, before and after each card (the eval's
        // outbound-text check, digits in any form).
        var seed = SeedFacts.Seed;
        long[] balances =
        [
            .. seed.Users.Select(u => u.Balance),
            hammoudiCard.BalanceAfterIqd, numberCard.BalanceAfterIqd, billCard.BalanceAfterIqd, purchaseCard.BalanceAfterIqd,
        ];
        var verdict = OutboundTextCheck.Check(
            texts, [.. seed.Accounts.Select(a => a.Number)], [.. seed.Bills.Select(b => b.SubscriberNumber.Replace(" ", string.Empty, StringComparison.Ordinal))], balances);
        Assert.True(verdict.Ok, "Outbound text holds a " + verdict.Kind + ".");

        // Absent: every saved name, alias and registered name of the seed, and every PIN.
        var normalizedPayload = ArabicText.Normalize(payload);
        string?[] names =
        [
            .. seed.Contacts.Select(c => c.SavedName), .. seed.Contacts.Select(c => c.Alias), .. seed.Accounts.Select(a => a.RegisteredName),
            .. seed.Users.Select(u => u.Name),
        ];
        Assert.All(names.Where(n => !string.IsNullOrWhiteSpace(n)), name =>
            Assert.DoesNotContain(ArabicText.Normalize(name!), normalizedPayload, StringComparison.Ordinal));
        Assert.All(seed.Users, u => Assert.DoesNotContain(u.Pin, payload, StringComparison.Ordinal));
        Assert.DoesNotContain(ArabicText.Normalize(SavedAliasText), normalizedPayload, StringComparison.Ordinal);

        // Absent: the purchase code, every order code returned and every seeded purchase code, as typed and in
        // Arabic-Indic digits; in particular the text sent with the message after the purchase (its pending note).
        if (confirmPurchase)
        {
            string[] codes =
            [
                .. purchaseCodes,
                .. seed.History.Where(h => h.Type == "purchase").Select(h => SeedData.OrderCodeFor(h.Id)),
            ];
            string[] forms = [.. codes.SelectMany(c => new[] { c, ArabicDigits.ToArabicIndic(c) }).Distinct(StringComparer.Ordinal)];
            Assert.All(forms, form => Assert.DoesNotContain(form, payload, StringComparison.Ordinal));

            var afterPurchase = string.Join('\n', client.Calls[callsBeforeBalance].Messages.Select(m => m.Text));
            Assert.All(forms, form => Assert.DoesNotContain(form, afterPurchase, StringComparison.Ordinal));
        }
    }
}
