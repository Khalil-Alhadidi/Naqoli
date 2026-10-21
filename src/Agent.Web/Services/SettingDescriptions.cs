namespace Agent.Web.Services;

/// <summary>
/// What the settings component says about one editable setting (UI pass, task 6): a label, one sentence, the unit, and
/// the range and default of its options class. <see cref="Name"/> is the row's name: an agent setting's bare name, or a
/// console setting's full key (<c>Console:GridPageSize</c>).
/// </summary>
public sealed record SettingDescription(string Name, string Label, string Description, string Unit, long Min, long Max, long Default);

/// <summary>
/// The English descriptions of the editable settings, for Khalil's review: exactly <see cref="RuntimeSettings.EditableNames"/>
/// and <see cref="RuntimeSettings.EditableConsoleNames"/> under <c>Console:</c>. <c>SettingDescriptionsTests</c> pins each
/// range and default to the <c>[Range]</c> attribute and the initial value of <c>AgentOptions</c> or
/// <see cref="ConsoleOptions"/>.
/// </summary>
public static class SettingDescriptions
{
    /// <summary>The descriptions by row name, in the order the settings component shows the rows.</summary>
    public static IReadOnlyDictionary<string, SettingDescription> Editable { get; } = new SettingDescription[]
    {
        new("MinAmountIqd", "Minimum amount", "The smallest payment the agent will prepare a card for.", "IQD", 1, 1_000_000_000_000, 1_000),
        new("PerTransactionCapIqd", "Per-payment cap", "The largest amount of one payment, before the fee.", "IQD", 1, 1_000_000_000_000, 5_000_000),
        new("PerDayCapIqd", "Daily cap", "The most a user may pay in one Iraq day, fees excluded.", "IQD", 1, 1_000_000_000_000, 10_000_000),
        new("CardTimerSeconds", "Card timer", "How long a confirmation card stays open before it expires.", "seconds", 1, 3_600, 45),
        new("MaxProductsInOneQuestion", "Products per question", "The most store products one question offers to tap.", "count", 1, 50, 8),
        new("RepeatWarningWindowMinutes", "Repeat warning window", "A payment like one made within this window carries a repeat warning on its card.", "minutes", 0, 1_440, 10),
        new("HistoryDays", "History window", "How far back the agent reads payments for the daily cap and the repeat warning.", "days", 1, 3_650, 90),
        new("MinSubstringWordLength", "Shortest partial word", "A typed word shorter than this must equal a whole word of a saved name; longer words may match inside one.", "characters", 1, 20, 3),
        new("MaxYesNoWords", "Yes or no word limit", "A message of only yes or no words counts as a yes or a no when it has at most this many words.", "words", 1, 20, 4),
        new("LogPageSize", "Log rows loaded", "How many of the newest audit rows the log loads; the grid page size splits them into pages.", "rows", 1, 1_000, 100),
        new("CheckingRetrySeconds", "Checking retry", "While a card is checking, the phone asks the wallet again every this many seconds.", "seconds", 1, 600, 5),
        new("MaxAliasLength", "Alias length", "The longest alias text that can be saved.", "characters", 1, 100, 100),
        new("MaxMessageChars", "Message length", "The longest typed message the agent reads; a longer one is refused as not understood.", "characters", 1, 32_000, 500),
        new("MaxRequestsPerMessage", "Requests per message", "The most requests of one message that get cards; the rest are named in the reply.", "count", 1, 20, 3),
        new("MaxNamedNotPrepared", "Requests named", "The most of the leftover requests the reply names.", "count", 1, 20, 5),
        new("Console:GridPageSize", "Grid page size", "How many rows each console grid shows per page.", "rows", 5, 100, 10),
    }.ToDictionary(d => d.Name, StringComparer.Ordinal);
}
