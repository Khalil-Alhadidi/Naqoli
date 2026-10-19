extern alias AgentWeb;

using Agent.Core.Confirm;
using AgentWeb::Agent.Web.Services;
using AgentConversation = Agent.Core.Conversation.Conversation;

namespace Agent.Core.Tests.Conversation;

/// <summary>
/// Taps on the card shown (ruling 8 of milestone 9c: Confirm and Cancel name their card). Each passes the id of the card
/// shown when it is called, or <see cref="Guid.Empty"/> when none is, as the phone's card panel and the runner do.
/// </summary>
internal static class ConversationExtensions
{
    /// <summary>The Confirm button of the card shown.</summary>
    public static Task<ConfirmOutcome?> ConfirmShownAsync(this AgentConversation conversation, string? pin = null)
        => conversation.ConfirmAsync(conversation.Card?.PendingId ?? Guid.Empty, pin);

    /// <summary>The Cancel button of the card shown.</summary>
    public static Task CancelShownAsync(this AgentConversation conversation)
        => conversation.CancelAsync(conversation.Card?.PendingId ?? Guid.Empty);

    /// <summary>The Confirm button of the card the session shows.</summary>
    public static Task<ConfirmOutcome?> ConfirmShownAsync(this ChatSession session, string? pin = null)
        => session.ConfirmAsync(session.Card?.PendingId ?? Guid.Empty, pin);

    /// <summary>The Cancel button of the card the session shows.</summary>
    public static Task CancelShownAsync(this ChatSession session)
        => session.CancelAsync(session.Card?.PendingId ?? Guid.Empty);
}
