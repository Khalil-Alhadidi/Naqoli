using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Wallet.MockApi;

/// <summary>
/// Declares the wallet's two key headers in the OpenAPI document, so a reference page can offer a field for each.
/// Documentation only: the filters still enforce the keys.
/// </summary>
public static class OpenApiSecurity
{
    public const string ClientKeySchemeName = "ClientKey";
    public const string AdminKeySchemeName = "AdminKey";

    // The README's Development placeholder for both headers; with a key configured the person types the real one.
    public const string DevelopmentPlaceholderKey = "dev";

    public static OpenApiOptions AddWalletSecurity(this OpenApiOptions options)
    {
        options.AddDocumentTransformer((document, _, _) =>
        {
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes[ClientKeySchemeName] = HeaderScheme(
                ClientKeyFilter.HeaderName,
                "Every route except /health. In Development with no Wallet:ClientKey configured, any non-empty value is accepted.");
            document.Components.SecuritySchemes[AdminKeySchemeName] = HeaderScheme(
                AdminKeyFilter.HeaderName,
                "The /admin routes, with the client key. In Development with no Wallet:AdminKey configured, any non-empty value is accepted.");
            return Task.CompletedTask;
        });

        options.AddOperationTransformer((operation, context, _) =>
        {
            // RelativePath has no leading slash today; trim one anyway so the comparison does not depend on it.
            var path = "/" + (context.Description.RelativePath ?? string.Empty).TrimStart('/');

            if (path == "/health")
            {
                operation.Security = [];
                return Task.CompletedTask;
            }

            // The reference needs the host document: a requirement whose scheme does not resolve is not written.
            var requirement = new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(ClientKeySchemeName, context.Document)] = [],
            };
            if (path.StartsWith("/admin/", StringComparison.Ordinal) || path == "/admin")
            {
                requirement[new OpenApiSecuritySchemeReference(AdminKeySchemeName, context.Document)] = [];
            }

            operation.Security = [requirement];
            return Task.CompletedTask;
        });

        return options;
    }

    private static OpenApiSecurityScheme HeaderScheme(string headerName, string description) => new()
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Name = headerName,
        Description = description,
    };
}
