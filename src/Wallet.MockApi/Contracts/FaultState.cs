namespace Wallet.MockApi.Contracts;

/// <summary>The fault switch as <c>GET</c> and <c>POST /admin/faults</c> report it. <see cref="Mode"/> is camelCase.</summary>
public sealed record FaultState(string Mode, int DelayMs, bool Once);
