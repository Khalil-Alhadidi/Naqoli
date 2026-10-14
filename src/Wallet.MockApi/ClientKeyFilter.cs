using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Wallet.MockApi.Endpoints;

namespace Wallet.MockApi;

/// <summary>The problem code of the client key filter.</summary>
public static class ClientErrors
{
    public const string ClientKeyRequired = "client_key_required";
}

/// <summary>
/// Guards every wallet route except <c>/health</c>; it sits on the route group that the read, transaction and admin
/// endpoints hang off, so <c>/admin</c> needs this key and the admin key. With a <see cref="WalletOptions.ClientKey"/>
/// configured, the request must carry it in the <c>X-Wallet-Client-Key</c> header. With none configured, the routes are
/// open in Development only, and even there the request must carry that header with some non-empty value: a custom
/// header makes a browser send a CORS preflight, which this API never grants, so a web page on the demo machine cannot
/// move money, give consent or read a balance. In any other environment they are closed. Otherwise 403
/// <c>client_key_required</c>.
/// </summary>
public sealed class ClientKeyFilter(IOptionsMonitor<WalletOptions> options, IHostEnvironment environment) : IEndpointFilter
{
    public const string HeaderName = "X-Wallet-Client-Key";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var key = options.CurrentValue.ClientKey;
        var given = context.HttpContext.Request.Headers[HeaderName].ToString();
        if (string.IsNullOrEmpty(key))
        {
            return environment.IsDevelopment() && !string.IsNullOrEmpty(given)
                ? await next(context)
                : WalletProblems.Create(StatusCodes.Status403Forbidden, ClientErrors.ClientKeyRequired);
        }

        return Matches(given, key)
            ? await next(context)
            : WalletProblems.Create(StatusCodes.Status403Forbidden, ClientErrors.ClientKeyRequired);
    }

    // Constant-time for equal lengths, so the comparison does not reveal how much of the key was right.
    private static bool Matches(string given, string key)
        => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(key));
}
