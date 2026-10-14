namespace Wallet.MockApi.Contracts;

/// <summary>
/// The body of <c>POST /admin/faults</c>. <see cref="Mode"/> is <c>none</c>, <c>error</c>, <c>slow</c>,
/// <c>timeoutBeforeExecute</c> or <c>timeoutAfterExecute</c>. <see cref="DelayMs"/> defaults to
/// <c>Wallet:FaultDelayMs</c>. With <see cref="Once"/> the fault clears itself after one <c>POST /transactions</c>.
/// </summary>
public sealed record FaultRequest(string? Mode, int? DelayMs, bool Once = false);
