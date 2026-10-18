using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Agent.Core;
using Agent.Core.Wallet;
using Microsoft.Extensions.Options;

namespace Agent.Web.Services;

/// <summary>The wallet's fault switch as the wallet reports it: the mode name, the delay, and whether it fires once.</summary>
public sealed record WalletFaultState(string Mode, int DelayMs, bool Once);

/// <summary>What a fault-switch call gave: the state read back, or the failure's code or status.</summary>
public abstract record WalletFaultResult
{
    private WalletFaultResult()
    {
    }

    public sealed record Ok(WalletFaultState State) : WalletFaultResult;

    /// <param name="Problem">The wallet's <c>code</c>, else the HTTP status, else the reason the call got no answer.</param>
    public sealed record Failed(string Problem) : WalletFaultResult;
}

/// <summary>What an external debit gave: its reference and the balance after, or the failure's code or status.</summary>
public abstract record WalletDebitResult
{
    private WalletDebitResult()
    {
    }

    public sealed record Done(string Reference, long BalanceAfterIqd) : WalletDebitResult;

    /// <param name="Problem">The wallet's <c>code</c>, else the HTTP status, else the reason the call got no answer.</param>
    public sealed record Failed(string Problem) : WalletDebitResult;
}

/// <summary>The row counts the wallet reports after a reset, read back from its database.</summary>
public sealed record WalletResetCounts(
    int Users, int Accounts, int Contacts, int Aliases, int BillAccounts, int Products, int Transactions, int Orders);

/// <summary>What a wallet reset gave: the row counts read back, or the failure's code or status.</summary>
public abstract record WalletResetResult
{
    private WalletResetResult()
    {
    }

    public sealed record Ok(WalletResetCounts Counts) : WalletResetResult;

    /// <param name="Problem">The wallet's <c>code</c>, else the HTTP status, else the reason the call got no answer.</param>
    public sealed record Failed(string Problem) : WalletResetResult;
}

/// <summary>
/// The console's side door into the mock wallet (milestone 8), for the fault panel and the reset panel only: the fault
/// switch (<c>GET</c> and <c>POST admin/faults</c>), the external debit (<c>POST admin/debit</c>) and the reset
/// (<c>POST admin/reset</c>, milestone 7 post-tag). It names no other route, so it can never execute a payment (rule 1). The typed <see cref="HttpClient"/> carries the wallet's base address,
/// <c>X-Wallet-Client-Key</c> (the agent's key, or <c>dev</c> in Development, as <see cref="WalletClient"/> decides) and
/// <c>X-Wallet-Admin-Key</c> (<see cref="ConsoleOptions.WalletAdminKey"/>). Every failure comes back as a result with
/// the wallet's code or the HTTP status; nothing is thrown to a component, and no key is ever logged or shown.
/// </summary>
public sealed class WalletFaults(HttpClient http)
{
    /// <summary>The header the wallet's <c>AdminKeyFilter</c> reads.</summary>
    public const string AdminKeyHeader = "X-Wallet-Admin-Key";

    private const string FaultsRoute = "admin/faults";
    private const string DebitRoute = "admin/debit";

    /// <summary>The wallet's reset with no query: the seed restored and every user's consent cleared (Khalil, 18 Oct 2026).</summary>
    private const string ResetRoute = "admin/reset";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Sets the base address, the timeout and the two keys (the fault panel's client).</summary>
    public static void Configure(HttpClient http, AgentOptions agent, ConsoleOptions console, IHostEnvironment? environment)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(console);

        http.BaseAddress = agent.WalletBaseUrl;
        http.Timeout = TimeSpan.FromSeconds(agent.WalletTimeoutSeconds);
        var clientKey = agent.WalletClientKey
            ?? (environment?.IsDevelopment() == true ? AgentOptions.DevelopmentWalletClientKey : null);
        if (clientKey is not null)
            http.DefaultRequestHeaders.Add(WalletClient.ClientKeyHeader, clientKey);
        http.DefaultRequestHeaders.Add(AdminKeyHeader, console.WalletAdminKey);
    }

    /// <summary>The wallet's fault switch now.</summary>
    public Task<WalletFaultResult> GetAsync(CancellationToken cancellationToken = default)
        => FaultAsync(() => http.GetAsync(FaultsRoute, cancellationToken), cancellationToken);

    /// <summary>
    /// Sets the wallet's fault switch: <paramref name="mode"/> is <c>none</c>, <c>error</c>, <c>slow</c>,
    /// <c>timeoutBeforeExecute</c> or <c>timeoutAfterExecute</c>; a null <paramref name="delayMs"/> takes the wallet's default.
    /// </summary>
    public Task<WalletFaultResult> SetAsync(string mode, int? delayMs, bool once, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mode);
        return FaultAsync(() => http.PostAsJsonAsync(FaultsRoute, new { mode, delayMs, once }, Json, cancellationToken), cancellationToken);
    }

    /// <summary>Sets the wallet's fault switch to <c>none</c>.</summary>
    public Task<WalletFaultResult> ClearAsync(CancellationToken cancellationToken = default)
        => FaultAsync(() => http.PostAsJsonAsync(FaultsRoute, new { mode = "none" }, Json, cancellationToken), cancellationToken);

    /// <summary>Takes <paramref name="amountIqd"/> from <paramref name="userId"/>'s wallet from outside the agent.</summary>
    public async Task<WalletDebitResult> DebitAsync(int userId, long amountIqd, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await http.PostAsJsonAsync(DebitRoute, new { userId, amountIqd }, Json, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new WalletDebitResult.Failed(await ProblemAsync(response, cancellationToken));

            var debit = await response.Content.ReadFromJsonAsync<DebitBody>(Json, cancellationToken);
            return debit is { Reference: { } reference }
                ? new WalletDebitResult.Done(reference, debit.BalanceAfterIqd)
                : new WalletDebitResult.Failed("unreadable_answer");
        }
        catch (Exception ex) when (IsNoAnswer(ex, cancellationToken))
        {
            return new WalletDebitResult.Failed(NoAnswer(ex));
        }
    }

    /// <summary>
    /// Returns the wallet to the seed with every user not consented, and clears its fault switch (<c>POST admin/reset</c>,
    /// no query). The wallet's ids start again, so the caller closes the agent's open pending records after it.
    /// </summary>
    public async Task<WalletResetResult> ResetAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await http.PostAsync(ResetRoute, content: null, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new WalletResetResult.Failed(await ProblemAsync(response, cancellationToken));

            var counts = await response.Content.ReadFromJsonAsync<WalletResetCounts>(Json, cancellationToken);
            return counts is not null
                ? new WalletResetResult.Ok(counts)
                : new WalletResetResult.Failed("unreadable_answer");
        }
        catch (Exception ex) when (IsNoAnswer(ex, cancellationToken))
        {
            return new WalletResetResult.Failed(NoAnswer(ex));
        }
    }

    private async Task<WalletFaultResult> FaultAsync(Func<Task<HttpResponseMessage>> send, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await send();
            if (!response.IsSuccessStatusCode)
                return new WalletFaultResult.Failed(await ProblemAsync(response, cancellationToken));

            var state = await response.Content.ReadFromJsonAsync<WalletFaultState>(Json, cancellationToken);
            return state is { Mode: not null }
                ? new WalletFaultResult.Ok(state)
                : new WalletFaultResult.Failed("unreadable_answer");
        }
        catch (Exception ex) when (IsNoAnswer(ex, cancellationToken))
        {
            return new WalletFaultResult.Failed(NoAnswer(ex));
        }
    }

    /// <summary>The wallet's <c>code</c>, else the HTTP status as digits.</summary>
    private static async Task<string> ProblemAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var status = ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);
        try
        {
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("code", out var code)
                && code.ValueKind == JsonValueKind.String
                && code.GetString() is { Length: > 0 } value
                ? value
                : status;
        }
        catch (JsonException)
        {
            return status;
        }
    }

    /// <summary>
    /// No answer: the wallet is down, the connection dropped (also while the body was read), the client was disposed
    /// during circuit teardown, the call timed out, or the answer could not be read. Not the caller's cancellation.
    /// </summary>
    private static bool IsNoAnswer(Exception ex, CancellationToken cancellationToken)
        => ex is HttpRequestException or IOException or ObjectDisposedException or JsonException or NotSupportedException
            || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    private static string NoAnswer(Exception ex) => ex switch
    {
        TaskCanceledException => "timeout",
        HttpRequestException or IOException or ObjectDisposedException => "unreachable",
        _ => "unreadable_answer",
    };

    private sealed record DebitBody(string? Reference, long BalanceAfterIqd);
}
