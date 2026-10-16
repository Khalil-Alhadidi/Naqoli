extern alias AgentWeb;

using Agent.Core.Masking;
using Agent.Core.Parsing;
using Agent.Core.Requests;
using AgentWeb::Agent.Web.Services;

namespace Agent.Core.Tests.Web;

/// <summary>
/// The console's model fault (milestone 8): a decorator over the registered parser that, while the switch is armed,
/// throws <see cref="ParserUnavailableException"/> (unreachable) or returns an unclear request with problem
/// <c>injected</c> (invalid reply), as the runner's <c>RecordingParser</c> does. The parser underneath never sees the
/// faulted call.
/// </summary>
public class FaultingParserTests
{
    private const string Sentence = "send fifty thousand to my brother";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly ParseContext Context = new(null, MaskMap.Empty);

    private static readonly ParsedRequest Expected = ParsedRequest.Transfer("brother", 50, AmountUnit.Thousand);

    private sealed class CountingParser : IParser
    {
        public int Calls { get; private set; }

        public string Name => "counting";

        public Task<ParsedRequest> ParseAsync(string text, ParseContext context, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Expected);
        }
    }

    private static (FaultingParser Parser, CountingParser Inner, ModelFaultSwitch Switch) Create()
    {
        var inner = new CountingParser();
        var faults = new ModelFaultSwitch();
        return (new FaultingParser(inner, faults), inner, faults);
    }

    [Fact]
    public async Task WithoutAFaultTheCallIsDelegated()
    {
        var (parser, inner, _) = Create();

        Assert.Equal(Expected, await parser.ParseAsync(Sentence, Context, Token));
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task AnUnreachableFaultThrowsUnavailableOnceAndClears()
    {
        var (parser, inner, faults) = Create();
        var changed = 0;
        faults.Changed += () => changed++;
        faults.Set(ModelFaultMode.Unreachable, once: true);

        await Assert.ThrowsAsync<ParserUnavailableException>(() => parser.ParseAsync(Sentence, Context, Token));
        Assert.Equal(0, inner.Calls);
        Assert.Equal(ModelFaultMode.None, faults.Mode);

        Assert.Equal(Expected, await parser.ParseAsync(Sentence, Context, Token));
        Assert.Equal(1, inner.Calls);
        Assert.Equal(2, changed);
    }

    [Fact]
    public async Task AnInvalidReplyFaultIsUnclearWithProblemInjected()
    {
        var (parser, inner, faults) = Create();
        faults.Set(ModelFaultMode.InvalidReply, once: true);

        var parsed = await parser.ParseAsync(Sentence, Context, Token);

        Assert.Equal(new ParsedRequest { Intent = RequestIntent.Unclear, Problem = FaultingParser.InjectedProblem }, parsed);
        Assert.Equal("injected", FaultingParser.InjectedProblem);
        Assert.Equal(0, inner.Calls);
        Assert.Equal(ModelFaultMode.None, faults.Mode);
    }

    [Fact]
    public async Task AFaultNotSetToOnceHitsEveryCallUntilCleared()
    {
        var (parser, inner, faults) = Create();
        faults.Set(ModelFaultMode.Unreachable, once: false);

        await Assert.ThrowsAsync<ParserUnavailableException>(() => parser.ParseAsync(Sentence, Context, Token));
        await Assert.ThrowsAsync<ParserUnavailableException>(() => parser.ParseAsync(Sentence, Context, Token));
        Assert.Equal((ModelFaultMode.Unreachable, false), (faults.Mode, faults.Once));

        faults.Clear();
        Assert.Equal(Expected, await parser.ParseAsync(Sentence, Context, Token));
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public void TheNameIsTheInnerParsers()
    {
        var (parser, inner, _) = Create();

        Assert.Equal(inner.Name, parser.Name);
        Assert.Same(inner, parser.Inner);
    }

    [Fact]
    public async Task ASubscriberThatThrowsNeverReachesTheParsePath()
    {
        // Security review M5 of milestone 8: a panel whose circuit is tearing down must not break a user's message.
        var (parser, inner, faults) = Create();
        var after = 0;
        faults.Changed += () => throw new InvalidOperationException("Stub: a panel that is going away.");
        faults.Changed += () => after++;

        faults.Set(ModelFaultMode.Unreachable, once: true);
        Assert.Equal(1, after);

        await Assert.ThrowsAsync<ParserUnavailableException>(() => parser.ParseAsync(Sentence, Context, Token));
        Assert.Equal(2, after);
        Assert.Equal(Expected, await parser.ParseAsync(Sentence, Context, Token));
        Assert.Equal(1, inner.Calls);
    }

    [Theory]
    [InlineData(ModelFaultMode.None, "none")]
    [InlineData(ModelFaultMode.Unreachable, "unreachable")]
    [InlineData(ModelFaultMode.InvalidReply, "invalid_reply")]
    public void EachModeHasItsWireName(ModelFaultMode mode, string name)
    {
        Assert.Equal(name, ModelFaultSwitch.NameOf(mode));
        Assert.True(ModelFaultSwitch.TryParse(name, out var parsed));
        Assert.Equal(mode, parsed);
    }
}
