using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Wallet.MockApi.Endpoints;

namespace Wallet.MockApi;

/// <summary>
/// Guards every <c>/admin</c> route. With a <see cref="WalletOptions.AdminKey"/> configured, the request must carry it
/// in the <c>X-Wallet-Admin-Key</c> header. With none configured, the routes are open in Development only, and even
/// there the request must carry that header with some non-empty value: a custom header makes a browser send a CORS
/// preflight, which this API never grants, so a web page on the demo machine cannot reset the wallet or set a fault.
/// In any other environment they are closed. Otherwise 403 <c>admin_key_required</c>.
/// </summary>
public sealed class AdminKeyFilter(IOptionsMonitor<WalletOptions> options, IHostEnvironment environment) : IEndpointFilter
{
    public const string HeaderName = "X-Wallet-Admin-Key";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var key = options.CurrentValue.AdminKey;
        var given = context.HttpContext.Request.Headers[HeaderName].ToString();
        if (string.IsNullOrEmpty(key))
        {
            return environment.IsDevelopment() && !string.IsNullOrEmpty(given)
                ? await next(context)
                : WalletProblems.Create(StatusCodes.Status403Forbidden, AdminErrors.AdminKeyRequired);
        }

        return Matches(given, key)
            ? await next(context)
            : WalletProblems.Create(StatusCodes.Status403Forbidden, AdminErrors.AdminKeyRequired);
    }

    // Constant-time for equal lengths, so the comparison does not reveal how much of the key was right.
    private static bool Matches(string given, string key)
        => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(key));
}
