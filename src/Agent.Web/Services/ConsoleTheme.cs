using MudBlazor;

namespace Agent.Web.Services;

/// <summary>
/// The console's one MudBlazor theme, light only. Its palette takes the design tokens of <c>wwwroot/app.css</c>
/// (docs/agent-web.md, "Design tokens"); <c>DesignTokensTests.TheMudThemeUsesTheTokens</c> pins every value here equal
/// to its token, so a change of colour is made in both places or the test fails.
/// </summary>
public static class ConsoleTheme
{
    public static MudTheme Light { get; } = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#673b98",          // --color-brand
            PrimaryDarken = "#5b3486",    // --color-brand-hover
            PrimaryLighten = "#f0ebf5",   // --color-brand-tint
            Error = "#b42318",            // --color-danger
            Warning = "#9a6700",          // --color-warning
            Success = "#1a7f37",          // --color-success
            Info = "#1565c0",             // --zone-user
            TextPrimary = "#1f2328",      // --color-text
            TextSecondary = "#59636e",    // --color-text-muted
            Background = "#f6f7f9",       // --color-page
            Surface = "#ffffff",          // --color-surface
            LinesDefault = "#d0d7de",     // --color-border
            AppbarBackground = "#673b98", // --color-brand
            AppbarText = "#ffffff",       // --color-on-brand
            DrawerBackground = "#ffffff", // --color-surface
        },
        Typography = new Typography
        {
            // --font-console
            Default = new DefaultTypography
            {
                FontFamily = ["system-ui", "Segoe UI", "Roboto", "Helvetica", "Arial", "sans-serif"],
            },
        },
    };
}
