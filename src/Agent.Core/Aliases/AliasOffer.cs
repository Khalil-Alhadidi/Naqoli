namespace Agent.Core.Aliases;

/// <summary>
/// An offer to save an alias (rule 18, milestone 9a): <see cref="Text"/> as the user typed it, for the one account
/// <see cref="AccountId"/>, shown with <see cref="RecipientName"/> so the user sees who the name will point to before the
/// tap (review R6). Built by code from the user's own words and the wallet's data, never from model text (ruling 3).
/// </summary>
/// <param name="Text">The recipient as the user typed it, trimmed; <c>{0}</c> of <c>Offer_save_alias</c>.</param>
/// <param name="AccountId">The one account the alias would point to.</param>
/// <param name="RecipientName">
/// The wallet's registered name of the account, or, when the wallet holds none, the tapped candidate's text as stored (the
/// saved name or alias the option showed); <c>{1}</c> of <c>Offer_save_alias</c>.
/// </param>
public sealed record AliasOffer(string Text, int AccountId, string RecipientName);
