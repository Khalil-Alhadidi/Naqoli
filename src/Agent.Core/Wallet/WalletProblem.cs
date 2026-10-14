using System.Net.Http.Headers;
using System.Text.Json;

namespace Agent.Core.Wallet;

/// <summary>
/// A refusal from the wallet: the HTTP status and the ProblemDetails <c>code</c> extension, plus the figures some codes
/// carry. A body without <c>code</c> (an error raised by ASP.NET Core itself, or no body at all) gets <see cref="NoCode"/>.
/// </summary>
public sealed record WalletProblem(int Status, string Code, long? AmountDueIqd, int? AttemptsLeft)
{
    public const string NoCode = "no_code";

    /// <summary>Reads the problem from a response. Never throws on a missing or malformed body.</summary>
    internal static async Task<WalletProblem> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        if (!IsJson(response.Content.Headers.ContentType))
            return new WalletProblem(status, NoCode, null, null);

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return new WalletProblem(status, NoCode, null, null);

            var code = root.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(c.GetString())
                ? c.GetString()!
                : NoCode;
            long? amountDue = root.TryGetProperty("amountDueIqd", out var a) && a.TryGetInt64(out var due) ? due : null;
            int? attemptsLeft = root.TryGetProperty("attemptsLeft", out var l) && l.TryGetInt32(out var left) ? left : null;
            return new WalletProblem(status, code, amountDue, attemptsLeft);
        }
        catch (JsonException)
        {
            return new WalletProblem(status, NoCode, null, null);
        }
    }

    private static bool IsJson(MediaTypeHeaderValue? contentType)
        => contentType?.MediaType is { } media
           && (media.Equals("application/json", StringComparison.OrdinalIgnoreCase)
               || media.EndsWith("+json", StringComparison.OrdinalIgnoreCase));
}
