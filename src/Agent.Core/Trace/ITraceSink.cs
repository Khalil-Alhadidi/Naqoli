namespace Agent.Core.Trace;

/// <summary>
/// Receives the steps of the live trace. A step is a short code (<c>card_created</c>, <c>outcome:done</c>); its detail
/// holds codes, states and amounts only: never a PIN, an idempotency key, a purchase code, a phone number or any text
/// the user typed. Implementations must not throw.
/// </summary>
public interface ITraceSink
{
    void Record(TraceZone zone, string step, string? detail);
}
