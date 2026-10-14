using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Agent.Core.Wallet;

/// <summary>
/// The agent's only way to the mock wallet (docs/wallet-api.md), over HTTP. Reads and quotes return
/// <see cref="WalletResult{T}"/>; execute returns an <see cref="ExecuteOutcome"/>. It never retries, never logs a body,
/// and sends the PIN only inside the execute body.
/// </summary>
public sealed partial class WalletClient
{
    public const string ClientKeyHeader = "X-Wallet-Client-Key";

    public const string IdempotencyKeyHeader = "Idempotency-Key";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient http;
    private readonly ILogger<WalletClient> logger;

    public WalletClient(HttpClient http, ILogger<WalletClient> logger)
    {
        this.http = http;
        this.logger = logger;
    }

    /// <summary>
    /// Sets the base address, the timeout and the client key header of <paramref name="http"/>. The key is
    /// <see cref="AgentOptions.WalletClientKey"/>, or <see cref="AgentOptions.DevelopmentWalletClientKey"/> when none is
    /// configured and <paramref name="environment"/> is Development; with neither, no header is sent and the wallet refuses.
    /// </summary>
    internal static void Configure(HttpClient http, AgentOptions options, IHostEnvironment? environment)
    {
        http.BaseAddress = options.WalletBaseUrl;
        http.Timeout = TimeSpan.FromSeconds(options.WalletTimeoutSeconds);

        var key = options.WalletClientKey
            ?? (environment?.IsDevelopment() == true ? AgentOptions.DevelopmentWalletClientKey : null);
        if (key is not null)
            http.DefaultRequestHeaders.Add(ClientKeyHeader, key);
    }

    public Task<WalletResult<WalletUser>> GetUserAsync(int userId, CancellationToken cancellationToken = default)
        => GetAsync<WalletUser>("user", FormattableString.Invariant($"users/{userId}"), cancellationToken);

    public Task<WalletResult<WalletAccount>> GetAccountByPhoneAsync(string phone, CancellationToken cancellationToken = default)
        => GetAsync<WalletAccount>("account", "accounts/by-phone/" + Uri.EscapeDataString(phone), cancellationToken);

    public Task<WalletResult<WalletContacts>> GetContactsAsync(int userId, CancellationToken cancellationToken = default)
        => GetAsync<WalletContacts>("contacts", FormattableString.Invariant($"users/{userId}/contacts"), cancellationToken);

    public Task<WalletResult<IReadOnlyList<WalletBill>>> GetBillsAsync(int userId, CancellationToken cancellationToken = default)
        => GetAsync<IReadOnlyList<WalletBill>>("bills", FormattableString.Invariant($"users/{userId}/bills"), cancellationToken);

    public Task<WalletResult<WalletBalance>> GetBalanceAsync(int userId, CancellationToken cancellationToken = default)
        => GetAsync<WalletBalance>("balance", FormattableString.Invariant($"users/{userId}/balance"), cancellationToken);

    /// <summary>The transactions <paramref name="userId"/> paid, newest first; <paramref name="toPhone"/> keeps transfers to that phone only.</summary>
    public Task<WalletResult<IReadOnlyList<WalletHistoryItem>>> GetHistoryAsync(
        int userId, DateTimeOffset? since, string? toPhone, int take, CancellationToken cancellationToken = default)
    {
        var query = new List<string> { "take=" + take.ToString(CultureInfo.InvariantCulture) };
        if (since is { } from)
            query.Add("since=" + Uri.EscapeDataString(from.ToString("O", CultureInfo.InvariantCulture)));
        if (toPhone is not null)
            query.Add("to=" + Uri.EscapeDataString(toPhone));

        return GetAsync<IReadOnlyList<WalletHistoryItem>>(
            "history",
            FormattableString.Invariant($"users/{userId}/history?") + string.Join('&', query), cancellationToken);
    }

    /// <summary>The store catalogue, filtered by whichever of provider code, type and face value are given.</summary>
    public Task<WalletResult<IReadOnlyList<WalletProduct>>> GetProductsAsync(
        string? provider, string? type, long? value, CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        if (provider is not null)
            query.Add("provider=" + Uri.EscapeDataString(provider));
        if (type is not null)
            query.Add("type=" + Uri.EscapeDataString(type));
        if (value is { } face)
            query.Add("value=" + face.ToString(CultureInfo.InvariantCulture));

        var path = query.Count == 0 ? "store/products" : "store/products?" + string.Join('&', query);
        return GetAsync<IReadOnlyList<WalletProduct>>("products", path, cancellationToken);
    }

    /// <summary>Fee, total and balance after for a payment that has not happened. No money moves.</summary>
    public async Task<WalletResult<WalletQuote>> QuoteAsync(WalletPaymentRequest request, CancellationToken cancellationToken = default)
    {
        // A quote ignores the PIN, so it never carries one.
        using var response = await http.PostAsJsonAsync("quotes", request with { Pin = null }, Json, cancellationToken);
        return await ReadResultAsync<WalletQuote>(response, "quotes", cancellationToken);
    }

    /// <summary>Records that the user has given consent. Idempotent.</summary>
    public async Task<WalletResult<bool>> GiveConsentAsync(int userId, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsync(FormattableString.Invariant($"users/{userId}/consent"), content: null, cancellationToken);
        if (response.IsSuccessStatusCode)
            return new WalletResult<bool>.Ok(true);

        return await RefusedOrThrowAsync<bool>(response, "consent", cancellationToken);
    }

    /// <summary>The outcome stored under <paramref name="key"/> for <paramref name="userId"/>; 404 <c>unknown_key</c> for any other user.</summary>
    public Task<WalletResult<WalletTransaction>> FindByKeyAsync(string key, int userId, CancellationToken cancellationToken = default)
        => GetAsync<WalletTransaction>(
            "by-key",
            "transactions/by-key/" + Uri.EscapeDataString(key) + FormattableString.Invariant($"?userId={userId}"),
            cancellationToken);

    /// <summary>
    /// Sends one <c>POST /transactions</c> under <paramref name="idempotencyKey"/>. Only the Confirm handler calls this
    /// (rule 1). A cancellation by <paramref name="cancellationToken"/> propagates; every other failure is an outcome.
    /// </summary>
    internal async Task<ExecuteOutcome> ExecuteAsync(
        string idempotencyKey, WalletPaymentRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "transactions")
        {
            Content = JsonContent.Create(request, options: Json),
        };
        message.Headers.Add(IdempotencyKeyHeader, idempotencyKey);

        try
        {
            using var response = await http.SendAsync(message, cancellationToken);
            var status = (int)response.StatusCode;

            if (status is 200 or 201)
            {
                var transaction = await response.Content.ReadFromJsonAsync<WalletTransaction>(Json, cancellationToken);
                if (transaction is null)
                {
                    LogExecuteUnknown(status, "unreadable_response");
                    return new ExecuteOutcome.Unknown("unreadable_response");
                }

                LogExecuteDone(status);
                return new ExecuteOutcome.Done(transaction, Replayed: status == 200);
            }

            if (status is >= 400 and < 500)
            {
                var problem = await WalletProblem.ReadAsync(response, cancellationToken);
                LogExecuteRefused(status, problem.Code);
                return new ExecuteOutcome.Refused(problem);
            }

            // A 5xx, with or without a code, or any other status: the client cannot know whether money moved.
            var reason = "http_" + status.ToString(CultureInfo.InvariantCulture);
            LogExecuteUnknown(status, reason);
            return new ExecuteOutcome.Unknown(reason);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogExecuteUnknown(0, "timeout");
            return new ExecuteOutcome.Unknown("timeout");
        }
        catch (HttpRequestException)
        {
            LogExecuteUnknown(0, "connection");
            return new ExecuteOutcome.Unknown("connection");
        }
        catch (JsonException)
        {
            LogExecuteUnknown(0, "unreadable_response");
            return new ExecuteOutcome.Unknown("unreadable_response");
        }
    }

    // The route name, not the path, goes to the log, so a phone number or key in the path never reaches a log line.
    private async Task<WalletResult<T>> GetAsync<T>(string route, string path, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(path, cancellationToken);
        return await ReadResultAsync<T>(response, route, cancellationToken);
    }

    private async Task<WalletResult<T>> ReadResultAsync<T>(HttpResponseMessage response, string route, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            var value = await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken)
                ?? throw new InvalidOperationException($"The wallet answered {route} with an empty body.");
            return new WalletResult<T>.Ok(value);
        }

        return await RefusedOrThrowAsync<T>(response, route, cancellationToken);
    }

    // A 4xx is the wallet's answer; anything else on a read is a failure the caller handles (a read moves no money).
    private async Task<WalletResult<T>> RefusedOrThrowAsync<T>(HttpResponseMessage response, string route, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        if (status is >= 400 and < 500)
        {
            var problem = await WalletProblem.ReadAsync(response, cancellationToken);
            LogReadRefused(route, status, problem.Code);
            return new WalletResult<T>.Refused(problem);
        }

        LogReadFailed(route, status);
        response.EnsureSuccessStatusCode();
        throw new HttpRequestException($"The wallet answered {route} with status {status}.", null, response.StatusCode);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Wallet execute done with status {Status}")]
    private partial void LogExecuteDone(int status);

    [LoggerMessage(Level = LogLevel.Information, Message = "Wallet execute refused with status {Status} and code {Code}")]
    private partial void LogExecuteRefused(int status, string code);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Wallet execute outcome unknown (status {Status}, reason {Reason})")]
    private partial void LogExecuteUnknown(int status, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Wallet {Route} refused with status {Status} and code {Code}")]
    private partial void LogReadRefused(string route, int status, string code);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Wallet {Route} failed with status {Status}")]
    private partial void LogReadFailed(string route, int status);
}
