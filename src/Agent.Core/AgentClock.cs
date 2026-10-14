namespace Agent.Core;

/// <summary>Iraq time. Daily limits count from midnight in Iraq, which keeps no daylight saving time.</summary>
public static class AgentClock
{
    public static readonly TimeSpan IraqOffset = TimeSpan.FromHours(3);

    /// <summary>Midnight at the start of the Iraq date of <paramref name="now"/>, at offset +03:00.</summary>
    public static DateTimeOffset StartOfIraqDay(DateTimeOffset now)
    {
        var local = now.ToOffset(IraqOffset);
        return new DateTimeOffset(local.Date, IraqOffset);
    }
}
