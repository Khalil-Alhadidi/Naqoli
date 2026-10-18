extern alias AgentWeb;

using AgentWeb::Agent.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Agent.Core.Tests.Web;

/// <summary>
/// The process-wide wallet-change notification (milestone 7 post-tag; Khalil, 18 Oct 2026): every handler is called, a
/// throwing handler stops neither the next one nor the caller, and an unsubscribed handler is not called.
/// </summary>
public class WalletChangesTests
{
    private static readonly WalletChange Alias = new(1, WalletChangeKind.Alias);

    private static WalletChanges Create() => new(NullLogger<WalletChanges>.Instance);

    [Fact]
    public void EveryHandlerIsCalledWithTheChange()
    {
        var changes = Create();
        var first = new List<WalletChange>();
        var second = new List<WalletChange>();
        changes.Changed += first.Add;
        changes.Changed += second.Add;

        changes.Raise(Alias);

        Assert.Equal([Alias], first);
        Assert.Equal([Alias], second);
    }

    [Fact]
    public void AThrowingHandlerDoesNotStopTheNextOneOrReachTheCaller()
    {
        var changes = Create();
        var after = new List<WalletChange>();
        changes.Changed += _ => throw new InvalidOperationException("Stub: a component gone.");
        changes.Changed += after.Add;

        var thrown = Record.Exception(() => changes.Raise(Alias));

        Assert.Null(thrown);
        Assert.Equal([Alias], after);
    }

    [Fact]
    public void AnUnsubscribedHandlerIsNotCalled()
    {
        var changes = Create();
        var kept = new List<WalletChange>();
        var removed = new List<WalletChange>();
        Action<WalletChange> handler = removed.Add;
        changes.Changed += kept.Add;
        changes.Changed += handler;
        changes.Changed -= handler;

        changes.Raise(Alias);

        Assert.Equal([Alias], kept);
        Assert.Empty(removed);
    }

    [Fact]
    public void NoHandlerIsNotAnError() => Create().Raise(Alias);

    [Fact]
    public void UserZeroConcernsEveryUser()
    {
        Assert.True(new WalletChange(WalletChange.EveryUser, WalletChangeKind.Reset).Concerns(3));
        Assert.True(new WalletChange(3, WalletChangeKind.Payment).Concerns(3));
        Assert.False(new WalletChange(2, WalletChangeKind.Payment).Concerns(3));
    }
}
