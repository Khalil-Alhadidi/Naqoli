using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace Agent.Eval.Running;

/// <summary>What an external debit did.</summary>
/// <param name="Status"><c>done</c> or <c>failed</c>.</param>
/// <param name="TransactionId">The wallet's transaction id of the debit, when done; the snapshot diff leaves it out.</param>
/// <param name="Problem">The HTTP status or wallet code of a failure.</param>
public sealed record ExternalDebitResult(string Status, int? TransactionId, string? Problem)
{
    public const string Done = "done";
    public const string Failed = "failed";
}

/// <summary>
/// The runner's side door into the mock wallet (<c>docs/wallet-api.md</c>): reset to the seed, the fault switch, the
/// orders count, and the external debit of fault <c>external_debit</c>. It is not the agent: the agent reaches the wallet
/// only through <c>WalletClient</c>. The <see cref="HttpClient"/> carries the wallet's base address and the
/// <c>X-Wallet-Client-Key</c> header; this class adds <c>X-Wallet-Admin-Key</c> to the admin calls. Nothing here is
/// ever logged with a key or a PIN.
/// </summary>
public sealed class WalletAdmin
{
    public const string AdminKeyHeader = "X-Wallet-Admin-Key";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient http;
    private readonly string adminKey;

    public WalletAdmin(HttpClient http, string adminKey)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentException.ThrowIfNullOrWhiteSpace(adminKey);
        this.http = http;
        this.adminKey = adminKey;
    }

    /// <summary>
    /// Returns the wallet to the seed and clears the fault switch (<c>POST /admin/reset?consent=seed</c>). The seed's
    /// consent values are kept, U1 to U4 consented and U5 not, as the answer keys were written for (Khalil, 18 Oct 2026).
    /// </summary>
    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        using var request = Admin(HttpMethod.Post, "admin/reset?consent=seed");
        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureAsync(response, "reset", cancellationToken);
    }

    /// <summary>Sets the fault switch: <paramref name="mode"/> is <c>error</c>, <c>slow</c>, <c>timeoutBeforeExecute</c> or <c>timeoutAfterExecute</c>.</summary>
    public async Task SetFaultAsync(string mode, int delayMs, bool once, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mode);
        using var request = Admin(HttpMethod.Post, "admin/faults");
        request.Content = JsonContent.Create(new { mode, delayMs, once }, options: Json);
        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureAsync(response, "fault", cancellationToken);
    }

    /// <summary>Sets the fault switch to <c>none</c>.</summary>
    public async Task ClearFaultAsync(CancellationToken cancellationToken)
    {
        using var request = Admin(HttpMethod.Post, "admin/faults");
        request.Content = JsonContent.Create(new { mode = "none" }, options: Json);
        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureAsync(response, "fault", cancellationToken);
    }

    /// <summary>How many store orders <paramref name="userId"/> has (purchase codes issued); the codes themselves are not read.</summary>
    public async Task<int> OrdersCountAsync(int userId, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(FormattableString.Invariant($"users/{userId}/orders"), cancellationToken);
        await EnsureAsync(response, "orders", cancellationToken);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        return document.RootElement.GetArrayLength();
    }

    /// <summary>
    /// Money leaves <paramref name="userId"/>'s wallet from outside while a card waits (<c>POST /admin/debit</c>, Khalil,
    /// 14 Oct 2026): the balance drops by exactly <paramref name="amountIqd"/>, and the wallet writes an
    /// <c>externalDebit</c> row whose id is returned so the case's diff leaves it out. A refusal is
    /// <see cref="ExternalDebitResult.Failed"/> with the wallet's code, or the HTTP status when there is none.
    /// </summary>
    public async Task<ExternalDebitResult> ExternalDebitAsync(int userId, long amountIqd, CancellationToken cancellationToken)
    {
        using var request = Admin(HttpMethod.Post, "admin/debit");
        request.Content = JsonContent.Create(new { userId, amountIqd }, options: Json);
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var problem = await CodeAsync(response, cancellationToken)
                ?? ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);
            return new ExternalDebitResult(ExternalDebitResult.Failed, null, problem);
        }

        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        return new ExternalDebitResult(ExternalDebitResult.Done, document.RootElement.GetProperty("transactionId").GetInt32(), null);
    }

    private HttpRequestMessage Admin(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add(AdminKeyHeader, adminKey);
        return request;
    }

    private static async Task EnsureAsync(HttpResponseMessage response, string what, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var code = await CodeAsync(response, cancellationToken);
        throw new HttpRequestException(
            string.Create(CultureInfo.InvariantCulture, $"Wallet {what} call failed: {(int)response.StatusCode} {code}"),
            null,
            response.StatusCode);
    }

    /// <summary>The wallet's <c>code</c> of a ProblemDetails response, or null when the body has none.</summary>
    private static async Task<string?> CodeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("code", out var value)
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            // No problem body.
            return null;
        }
    }
}
