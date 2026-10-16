namespace Wallet.MockApi.Contracts;

/// <summary>
/// The body of <c>POST /admin/debit</c> (Khalil, 14 Oct 2026): take <see cref="AmountIqd"/> from the default account of
/// user <see cref="UserId"/>, from outside the agent.
/// </summary>
public sealed record DebitRequest(int UserId, long AmountIqd);
