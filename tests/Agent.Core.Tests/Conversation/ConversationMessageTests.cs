using Agent.Core.Conversation;

namespace Agent.Core.Tests.Conversation;

/// <summary>Security review M2 of milestone 9b: the purchase code never appears in a message's string form.</summary>
public class ConversationMessageTests
{
    [Fact]
    public void TheStringFormOfADonePurchaseMessageHoldsNoCode()
    {
        const string code = "1234567890123";
        var message = new ConversationMessage(ConversationRole.Agent, ConversationKind.Outcome, OutcomeCodes.Done, 745_000, "ZC-0000000099", code);

        var text = message.ToString();

        Assert.DoesNotContain(code, text, StringComparison.Ordinal);
        Assert.Contains("PurchaseCode = ***", text, StringComparison.Ordinal);
        Assert.Contains("PurchaseCode = none", (message with { PurchaseCode = null }).ToString(), StringComparison.Ordinal);
    }
}
