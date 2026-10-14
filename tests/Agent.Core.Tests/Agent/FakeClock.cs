namespace Agent.Core.Tests.Agent;

/// <summary>A clock the tests move by hand. It starts at the real time when it is created.</summary>
public sealed class FakeClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = TimeProvider.System.GetUtcNow();

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}
