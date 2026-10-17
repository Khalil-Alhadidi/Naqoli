namespace Agent.Core.Aliases;

/// <summary>
/// An offer to save an alias (rule 18, milestone 9a): <see cref="Text"/> as the user typed it, for the one account
/// <see cref="AccountId"/>, shown with the wallet's <see cref="RegisteredName"/> for that account. Built by code from the
/// user's own words and the wallet's data, never from model text (ruling 3).
/// </summary>
public sealed record AliasOffer(string Text, int AccountId, string? RegisteredName);
