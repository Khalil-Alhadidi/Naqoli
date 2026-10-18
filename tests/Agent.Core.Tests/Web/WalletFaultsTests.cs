extern alias AgentWeb;

using System.Net;
using AgentWeb::Agent.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Agent.Core.Tests.Web;

/// <summary>
/// Security review M1 of milestone 8: the console's side door never throws into the fault panel. A connection that
/// drops while the answer is read, or a handler disposed during circuit teardown, is a failure result with a fixed
/// reason, never an exception (an unhandled one ends a Blazor circuit).
/// </summary>
public class WalletFaultsTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>Answers 200 with a body whose stream fails on the first read, as a connection dropped mid-body does.</summary>
    private sealed class BrokenBodyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new BrokenStream()) });
    }

    /// <summary>Throws what it is given from SendAsync.</summary>
    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw exception;
    }

    private sealed class BrokenStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => 0; set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("Stub: the connection dropped.");

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static WalletFaults Faults(HttpMessageHandler handler)
        => new(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") }, new WalletChanges(NullLogger<WalletChanges>.Instance));

    [Fact]
    public async Task AConnectionDroppedWhileTheBodyIsReadIsUnreachable()
    {
        var faults = Faults(new BrokenBodyHandler());

        Assert.Equal(new WalletFaultResult.Failed("unreachable"), await faults.GetAsync(Token));
        Assert.Equal(new WalletDebitResult.Failed("unreachable"), await faults.DebitAsync(1, 5_000, Token));
        Assert.Equal(new WalletResetResult.Failed("unreachable"), await faults.ResetAsync(Token));
    }

    [Fact]
    public async Task AnIOExceptionFromTheHandlerIsUnreachable()
    {
        var faults = Faults(new ThrowingHandler(new IOException("Stub: reset.")));

        Assert.Equal(new WalletFaultResult.Failed("unreachable"), await faults.SetAsync("error", null, once: true, Token));
        Assert.Equal(new WalletDebitResult.Failed("unreachable"), await faults.DebitAsync(1, 5_000, Token));
    }

    [Fact]
    public async Task ADisposedHandlerIsUnreachable()
    {
        var faults = Faults(new ThrowingHandler(new ObjectDisposedException("HttpMessageHandler")));

        Assert.Equal(new WalletFaultResult.Failed("unreachable"), await faults.ClearAsync(Token));
        Assert.Equal(new WalletResetResult.Failed("unreachable"), await faults.ResetAsync(Token));
    }
}
