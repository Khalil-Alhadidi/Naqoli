using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Wallet.MockApi.Endpoints;

namespace Wallet.MockApi;

/// <summary>
/// Guards every <c>/admin</c> route. With no <see cref="WalletOptions.AdminKey"/> configured, the routes are open in
/// Development only; in any other environment they are closed. With a key configured, the request must carry it in
/// the <c>X-Wallet-Admin-Key</c> header. Otherwise 403 <c>admin_key_required</c>.
/// </summary>
public sealed class AdminKeyFilter(IOptionsMonitor<WalletOptions> options, IHostEnvironment environment) : IEndpointFilter
{
    public const string HeaderName = "X-Wallet-Admin-Key";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var key = options.CurrentValue.AdminKey;
        if (string.IsNullOrEmpty(key))
        {
            return environment.IsDevelopment()
                ? await next(context)
                : WalletProblems.Create(StatusCodes.Status403Forbidden, AdminErrors.AdminKeyRequired);
        }

        var given = context.HttpContext.Request.Headers[HeaderName].ToString();
        return Matches(given, key)
            ? await next(context)
            : WalletProblems.Create(StatusCodes.Status403Forbidden, AdminErrors.AdminKeyRequired);
    }

    // Constant-time for equal lengths, so the comparison does not reveal how much of the key was right.
    private static bool Matches(string given, string key)
        => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(key));
}
