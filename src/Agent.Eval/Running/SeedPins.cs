using System.Text.Json;
using System.Text.RegularExpressions;

namespace Agent.Eval.Running;

/// <summary>
/// The seed facts the runner needs at run time, read from Khalil's synthetic seed files: each user's PIN and number
/// (<c>users.json</c>), every wallet number (<c>accounts.json</c>), every bill subscriber number (<c>bills.json</c>),
/// the transfer fee (<c>settings.json</c>) and the number of account <c>X01</c>, the external debit's target. A PIN is
/// handed to the Confirm call and never written to a report or a log.
/// </summary>
public sealed partial class SeedPins
{
    /// <summary>The seed account an external debit pays (a number with a wallet that belongs to no user).</summary>
    public const string ExternalDebitAccountId = "X01";

    private readonly Dictionary<string, (string Pin, string Number)> users;

    private SeedPins(
        Dictionary<string, (string Pin, string Number)> users,
        IReadOnlyList<string> accountNumbers,
        IReadOnlyList<string> subscriberNumbers,
        long transferFeeIqd,
        string externalDebitPhone)
    {
        this.users = users;
        AllAccountNumbers = accountNumbers;
        SubscriberNumbers = subscriberNumbers;
        TransferFeeIqd = transferFeeIqd;
        ExternalDebitPhone = externalDebitPhone;
    }

    /// <summary>Every wallet number of the seed (users' and others'), 11 digits.</summary>
    public IReadOnlyList<string> AllAccountNumbers { get; }

    /// <summary>Every bill subscriber number of the seed, digits only.</summary>
    public IReadOnlyList<string> SubscriberNumbers { get; }

    /// <summary>The flat transfer fee, in IQD.</summary>
    public long TransferFeeIqd { get; }

    /// <summary>The number of seed account <see cref="ExternalDebitAccountId"/>.</summary>
    public string ExternalDebitPhone { get; }

    /// <summary>The seed user codes (<c>U1</c>...).</summary>
    public IEnumerable<string> UserCodes => users.Keys;

    /// <summary>Reads the seed files of <paramref name="seedDirectory"/>.</summary>
    /// <exception cref="InvalidDataException">A file is missing a field the runner needs.</exception>
    public static SeedPins Load(string seedDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(seedDirectory);

        var users = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        using (var document = Read(seedDirectory, "users.json"))
        {
            foreach (var user in document.RootElement.EnumerateArray())
                users[Text(user, "id")] = (Text(user, "pin"), Text(user, "number"));
        }

        var numbers = new List<string>();
        string? externalPhone = null;
        using (var document = Read(seedDirectory, "accounts.json"))
        {
            foreach (var account in document.RootElement.EnumerateArray())
            {
                var number = Text(account, "number");
                numbers.Add(number);
                if (account.TryGetProperty("id", out var id) && id.GetString() == ExternalDebitAccountId)
                    externalPhone = number;
            }
        }

        foreach (var (_, number) in users.Values)
        {
            if (!numbers.Contains(number, StringComparer.Ordinal))
                numbers.Add(number);
        }

        var subscribers = new List<string>();
        using (var document = Read(seedDirectory, "bills.json"))
        {
            foreach (var bill in document.RootElement.EnumerateArray())
            {
                if (bill.TryGetProperty("subscriber_number", out var subscriber) && subscriber.GetString() is { } value)
                    subscribers.Add(NotDigits().Replace(value, string.Empty));
            }
        }

        long fee;
        using (var document = Read(seedDirectory, "settings.json"))
            fee = document.RootElement.GetProperty("fees").GetProperty("transfer").GetInt64();

        return new SeedPins(
            users,
            numbers,
            subscribers,
            fee,
            externalPhone ?? throw new InvalidDataException("accounts.json has no account " + ExternalDebitAccountId + "."));
    }

    /// <summary>The seed PIN of <paramref name="userCode"/>.</summary>
    public string PinOf(string userCode) => Lookup(userCode).Pin;

    /// <summary>The wallet number of <paramref name="userCode"/>.</summary>
    public string NumberOf(string userCode) => Lookup(userCode).Number;

    /// <summary>A PIN that is not <paramref name="userCode"/>'s (the runner's "wrong" PIN): <c>0000</c>, or <c>1111</c> when that is the real one.</summary>
    public string WrongPinOf(string userCode) => PinOf(userCode) == "0000" ? "1111" : "0000";

    private (string Pin, string Number) Lookup(string userCode)
        => users.TryGetValue(userCode, out var user)
            ? user
            : throw new KeyNotFoundException("No seed user " + userCode + ".");

    private static JsonDocument Read(string directory, string file)
    {
        var path = Path.Combine(directory, file);
        if (!File.Exists(path))
            throw new InvalidDataException("The seed file " + file + " is missing from " + directory + ".");
        using var stream = File.OpenRead(path);
        return JsonDocument.Parse(stream);
    }

    private static string Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new InvalidDataException("A seed row has no string '" + name + "'.");

    [GeneratedRegex("[^0-9]")]
    private static partial Regex NotDigits();
}
