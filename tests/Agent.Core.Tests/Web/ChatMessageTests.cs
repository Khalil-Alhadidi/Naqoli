extern alias AgentWeb;

using AgentWeb::Agent.Web.Services;

namespace Agent.Core.Tests.Web;

/// <summary>Security review M2 of milestone 9b: the copied code and the phone text never appear in a message's string form.</summary>
public class ChatMessageTests
{
    [Fact]
    public void TheStringFormOfACodeMessageHoldsNoCodeAndNoText()
    {
        const string code = "1234567890123";
        const string body = "code message " + code;
        var message = new ChatMessage(7, ChatRole.Agent, body, ChatMessageKind.Outcome, "done", ChatAction.CopyCode, code);

        var text = message.ToString();

        Assert.DoesNotContain(code, text, StringComparison.Ordinal);
        Assert.DoesNotContain(body, text, StringComparison.Ordinal);
        Assert.Contains("CopyText = ***", text, StringComparison.Ordinal);
        Assert.Contains("CopyText = none", (message with { CopyText = null }).ToString(), StringComparison.Ordinal);
    }
}
