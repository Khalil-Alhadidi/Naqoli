using Agent.Core.Conversation;
using Agent.Core.Pipeline;
using Agent.Core.Requests;

namespace Agent.Core.Tests.Conversation;

/// <summary>
/// Security review M2 of milestone 9b: the purchase code never appears in a message's string form; nor, from milestone
/// 9c, the user's words for the parts that were not prepared (rule 22), of which it prints the count only.
/// </summary>
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

    [Fact]
    public void TheStringFormOfANotPreparedReplyHoldsTheCountAndNoName()
    {
        string[] names = ["zzyzx-one", "zzyzx-two", "zzyzx-three"];
        var message = new ConversationMessage(
            ConversationRole.Agent,
            ConversationKind.Info,
            ReasonCodes.MaxThreeRequests,
            NotPrepared: [.. names.Select(n => new NotPreparedPart(RequestIntent.Transfer, n))]);

        var text = message.ToString();

        Assert.All(names, name => Assert.DoesNotContain(name, text, StringComparison.Ordinal));
        Assert.Contains("NotPrepared = 3", text, StringComparison.Ordinal);
        Assert.Contains("NotPrepared = none", (message with { NotPrepared = null }).ToString(), StringComparison.Ordinal);
    }
}
