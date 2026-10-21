extern alias AgentWeb;

using System.Globalization;
using System.Text.RegularExpressions;
using Agent.Core.Tests.Wallet;
using AgentWeb::Agent.Web.Services;
using MudBlazor;
using MudBlazor.Utilities;

namespace Agent.Core.Tests.Web;

/// <summary>
/// UI pass task 1 (21 Oct 2026): every colour of the console is a token in the one <c>:root</c> block of
/// <c>wwwroot/app.css</c>, the token pairings of the table in <c>docs/agent-web.md</c> ("Design tokens") meet WCAG AA,
/// the MudBlazor theme takes its palette from the same values, and no font is loaded from another host.
/// </summary>
public partial class DesignTokensTests
{
    private static string WebRoot => Path.Combine(SeedHashTests.RepositoryRoot(), "src", "Agent.Web");

    private static string AppCssPath => Path.Combine(WebRoot, "wwwroot", "app.css");

    // The CSS named colours (CSS Color Module Level 4). transparent and currentcolor are keywords, not colours.
    private static readonly HashSet<string> NamedColours = new(StringComparer.OrdinalIgnoreCase)
    {
        "aliceblue", "antiquewhite", "aqua", "aquamarine", "azure", "beige", "bisque", "black", "blanchedalmond",
        "blue", "blueviolet", "brown", "burlywood", "cadetblue", "chartreuse", "chocolate", "coral", "cornflowerblue",
        "cornsilk", "crimson", "cyan", "darkblue", "darkcyan", "darkgoldenrod", "darkgray", "darkgreen", "darkgrey",
        "darkkhaki", "darkmagenta", "darkolivegreen", "darkorange", "darkorchid", "darkred", "darksalmon",
        "darkseagreen", "darkslateblue", "darkslategray", "darkslategrey", "darkturquoise", "darkviolet", "deeppink",
        "deepskyblue", "dimgray", "dimgrey", "dodgerblue", "firebrick", "floralwhite", "forestgreen", "fuchsia",
        "gainsboro", "ghostwhite", "gold", "goldenrod", "gray", "green", "greenyellow", "grey", "honeydew", "hotpink",
        "indianred", "indigo", "ivory", "khaki", "lavender", "lavenderblush", "lawngreen", "lemonchiffon", "lightblue",
        "lightcoral", "lightcyan", "lightgoldenrodyellow", "lightgray", "lightgreen", "lightgrey", "lightpink",
        "lightsalmon", "lightseagreen", "lightskyblue", "lightslategray", "lightslategrey", "lightsteelblue",
        "lightyellow", "lime", "limegreen", "linen", "magenta", "maroon", "mediumaquamarine", "mediumblue",
        "mediumorchid", "mediumpurple", "mediumseagreen", "mediumslateblue", "mediumspringgreen", "mediumturquoise",
        "mediumvioletred", "midnightblue", "mintcream", "mistyrose", "moccasin", "navajowhite", "navy", "oldlace",
        "olive", "olivedrab", "orange", "orangered", "orchid", "palegoldenrod", "palegreen", "paleturquoise",
        "palevioletred", "papayawhip", "peachpuff", "peru", "pink", "plum", "powderblue", "purple", "rebeccapurple",
        "red", "rosybrown", "royalblue", "saddlebrown", "salmon", "sandybrown", "seagreen", "seashell", "sienna",
        "silver", "skyblue", "slateblue", "slategray", "slategrey", "snow", "springgreen", "steelblue", "tan", "teal",
        "thistle", "tomato", "turquoise", "violet", "wheat", "white", "whitesmoke", "yellow", "yellowgreen",
    };

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex CssComment();

    [GeneratedRegex(@":root\s*\{(?<body>[^}]*)\}")]
    private static partial Regex RootBlock();

    [GeneratedRegex(@"(?<name>--[a-z0-9-]+)\s*:\s*(?<value>[^;]+);")]
    private static partial Regex TokenDeclaration();

    // A declaration's value runs to a ';' or a '}'; a selector such as 'a:hover {' ends at '{' and is not matched.
    [GeneratedRegex(@"(?<property>[-a-zA-Z]+)\s*:\s*(?<value>[^;{}]+)(?=[;}])")]
    private static partial Regex Declaration();

    [GeneratedRegex(@"url\([^)]*\)")]
    private static partial Regex Url();

    [GeneratedRegex("\"[^\"]*\"|'[^']*'")]
    private static partial Regex Quoted();

    [GeneratedRegex(@"--[a-zA-Z0-9-]+")]
    private static partial Regex CustomPropertyName();

    [GeneratedRegex(@"#[0-9a-fA-F]{3,8}\b|\brgba?\(|\bhsla?\(")]
    private static partial Regex ColourFunctionOrHex();

    [GeneratedRegex(@"[a-zA-Z]+")]
    private static partial Regex Word();

    [GeneratedRegex(@"style\s*=\s*""(?<value>[^""]*)""|<style[^>]*>(?<value>.*?)</style>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex RazorStyle();

    [GeneratedRegex(@"<link\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex LinkTag();

    [GeneratedRegex(@"href\s*=\s*""(?<href>[^""]*)""", RegexOptions.IgnoreCase)]
    private static partial Regex Href();

    private static string WithoutComments(string css) => CssComment().Replace(css, string.Empty);

    private static Dictionary<string, string> Tokens()
    {
        var css = WithoutComments(File.ReadAllText(AppCssPath));
        var roots = RootBlock().Matches(css);
        Assert.Single(roots);
        return TokenDeclaration().Matches(roots[0].Groups["body"].Value)
            .ToDictionary(m => m.Groups["name"].Value, m => m.Groups["value"].Value.Trim(), StringComparer.Ordinal);
    }

    /// <summary>The colour literals in a stretch of CSS declarations, outside custom property names and strings.</summary>
    private static IEnumerable<string> ColourLiterals(string declarations)
    {
        foreach (Match declaration in Declaration().Matches(declarations))
        {
            if (declaration.Groups["property"].Value.StartsWith("--", StringComparison.Ordinal))
                continue;
            var value = Url().Replace(declaration.Groups["value"].Value, " ");
            value = Quoted().Replace(value, " ");
            value = CustomPropertyName().Replace(value, " ");
            foreach (Match literal in ColourFunctionOrHex().Matches(value))
                yield return $"{declaration.Value.Trim()} ({literal.Value})";
            foreach (Match word in Word().Matches(value))
            {
                if (NamedColours.Contains(word.Value))
                    yield return $"{declaration.Value.Trim()} ({word.Value})";
            }
        }
    }

    [Fact]
    public void EveryColourInTheStylesheetIsAToken()
    {
        var found = new List<string>();

        var appCss = WithoutComments(File.ReadAllText(AppCssPath));
        Assert.Single(RootBlock().Matches(appCss));
        found.AddRange(ColourLiterals(RootBlock().Replace(appCss, string.Empty)).Select(l => "app.css: " + l));

        foreach (var file in Directory.EnumerateFiles(Path.Combine(WebRoot, "Components"), "*.razor.css", SearchOption.AllDirectories))
        {
            var css = WithoutComments(File.ReadAllText(file));
            Assert.Empty(RootBlock().Matches(css));
            found.AddRange(ColourLiterals(css).Select(l => Path.GetFileName(file) + ": " + l));
        }

        foreach (var file in Directory.EnumerateFiles(Path.Combine(WebRoot, "Components"), "*.razor", SearchOption.AllDirectories))
        {
            var razor = File.ReadAllText(file);
            found.AddRange(ColourFunctionOrHex().Matches(razor).Select(m => Path.GetFileName(file) + ": " + m.Value));
            foreach (Match style in RazorStyle().Matches(razor))
            {
                var value = style.Groups["value"].Value;
                // A style attribute holds bare declarations; close the last one so it is matched.
                found.AddRange(ColourLiterals(value + ";").Select(l => Path.GetFileName(file) + ": " + l));
            }
        }

        Assert.Empty(found);
    }

    public static TheoryData<string, string> TextPairings => new()
    {
        // Brand: white text on it and on its hover and pressed shades; brand and body text on its tint.
        { "--color-on-brand", "--color-brand" },
        { "--color-on-brand", "--color-brand-hover" },
        { "--color-on-brand", "--color-brand-pressed" },
        { "--color-brand", "--color-surface" },
        { "--color-brand", "--color-brand-tint" },
        { "--color-text", "--color-brand-tint" },
        // Neutrals: body and muted text on white and on the page.
        { "--color-text", "--color-surface" },
        { "--color-text", "--color-page" },
        { "--color-text-muted", "--color-surface" },
        { "--color-text-muted", "--color-page" },
        // Semantic colours on white.
        { "--color-success", "--color-surface" },
        { "--color-warning", "--color-surface" },
        { "--color-danger", "--color-surface" },
        // The four zones on white.
        { "--zone-user", "--color-surface" },
        { "--zone-model", "--color-surface" },
        { "--zone-code", "--color-surface" },
        { "--zone-write", "--color-surface" },
        // Each badge text on its tint.
        { "--color-success", "--color-success-tint" },
        { "--color-warning", "--color-warning-tint" },
        { "--color-danger", "--color-danger-tint" },
        { "--color-text-muted", "--color-neutral-tint" },
    };

    [Theory]
    [MemberData(nameof(TextPairings))]
    public void EveryTextPairingMeetsAA(string foreground, string background)
    {
        var tokens = Tokens();
        var ratio = ContrastRatio(tokens[foreground], tokens[background]);
        Assert.True(ratio >= 4.5, $"{foreground} on {background}: {ratio:F2} is below 4.5");
    }

    [Fact]
    public void TheContrastRatioMatchesTheWcagFormula()
    {
        Assert.Equal(21.0, ContrastRatio("#000000", "#ffffff"), 2);
        Assert.Equal(1.0, ContrastRatio("#673b98", "#673b98"), 2);
        Assert.Equal(7.91, ContrastRatio("#ffffff", "#673b98"), 2);
    }

    [Fact]
    public void NoZoneIsPurpleAndEachDiffersFromTheBrand()
    {
        // UI pass task 3: the four zones read apart from the purple brand and from one another by hue.
        const double FromTheBrand = 40;
        // Amber and red are 22 degrees apart in hue and are told apart by lightness, icon and label (controller, 21 Oct 2026).
        const double BetweenZones = 20;
        var tokens = Tokens();
        var zones = new[] { "--zone-user", "--zone-model", "--zone-code", "--zone-write" };
        var brand = Hue(tokens["--color-brand"]);
        var failures = new List<string>();

        for (var i = 0; i < zones.Length; i++)
        {
            var hue = Hue(tokens[zones[i]]);
            if (HueDistance(hue, brand) < FromTheBrand)
                failures.Add($"{zones[i]} ({hue:F0}°) is within {FromTheBrand}° of --color-brand ({brand:F0}°)");
            for (var j = i + 1; j < zones.Length; j++)
            {
                var other = Hue(tokens[zones[j]]);
                if (HueDistance(hue, other) < BetweenZones)
                    failures.Add($"{zones[i]} ({hue:F0}°) is within {BetweenZones}° of {zones[j]} ({other:F0}°)");
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void TheHueMatchesTheHslFormula()
    {
        Assert.Equal(0, Hue("#ff0000"), 1);
        Assert.Equal(120, Hue("#00ff00"), 1);
        Assert.Equal(240, Hue("#0000ff"), 1);
        Assert.Equal(268, Hue("#673b98"), 0);
        Assert.Equal(20, HueDistance(350, 10), 1);
    }

    [Fact]
    public void TheMudThemeUsesTheTokens()
    {
        var tokens = Tokens();
        var palette = ConsoleTheme.Light.PaletteLight;
        var expected = new (string Property, MudColor Actual, string Token)[]
        {
            ("Primary", palette.Primary, "--color-brand"),
            ("PrimaryDarken", new MudColor(palette.PrimaryDarken), "--color-brand-hover"),
            ("PrimaryLighten", new MudColor(palette.PrimaryLighten), "--color-brand-tint"),
            ("Error", palette.Error, "--color-danger"),
            ("Warning", palette.Warning, "--color-warning"),
            ("Success", palette.Success, "--color-success"),
            ("Info", palette.Info, "--zone-user"),
            ("TextPrimary", palette.TextPrimary, "--color-text"),
            ("TextSecondary", palette.TextSecondary, "--color-text-muted"),
            ("Background", palette.Background, "--color-page"),
            ("Surface", palette.Surface, "--color-surface"),
            ("LinesDefault", palette.LinesDefault, "--color-border"),
            ("AppbarBackground", palette.AppbarBackground, "--color-brand"),
            ("AppbarText", palette.AppbarText, "--color-on-brand"),
            ("DrawerBackground", palette.DrawerBackground, "--color-surface"),
        };

        foreach (var (property, actual, token) in expected)
            Assert.True(new MudColor(tokens[token]) == actual, $"{property} is {actual}, {token} is {tokens[token]}");

        Assert.Equal(FontStack(tokens["--font-console"]), ConsoleTheme.Light.Typography.Default.FontFamily);
    }

    [Fact]
    public void NoFontIsLoadedFromTheNetwork()
    {
        var app = File.ReadAllText(Path.Combine(WebRoot, "Components", "App.razor"));
        var links = LinkTag().Matches(app).Select(m => m.Value).ToList();
        Assert.NotEmpty(links);

        foreach (var link in links)
        {
            var href = Href().Match(link);
            if (!href.Success)
                continue;
            var value = href.Groups["href"].Value;
            Assert.False(
                value.StartsWith("//", StringComparison.Ordinal) || Uri.TryCreate(value, UriKind.Absolute, out _),
                $"App.razor links to another host: {link}");
        }
    }

    private static string[] FontStack(string value)
        => value.Split(',').Select(f => f.Trim().Trim('"', '\'')).ToArray();

    private static double ContrastRatio(string foreground, string background)
    {
        var a = RelativeLuminance(foreground);
        var b = RelativeLuminance(background);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    /// <summary>The HSL hue of a <c>#rrggbb</c> colour, in degrees from 0 to 360 (0 for a grey).</summary>
    private static double Hue(string hex)
    {
        Assert.Matches("^#[0-9a-fA-F]{6}$", hex);
        double Channel(int offset) => int.Parse(hex.AsSpan(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
        var (r, g, b) = (Channel(1), Channel(3), Channel(5));
        var max = Math.Max(r, Math.Max(g, b));
        var delta = max - Math.Min(r, Math.Min(g, b));
        if (delta == 0)
            return 0;
        var hue = max == r ? (g - b) / delta % 6 : max == g ? ((b - r) / delta) + 2 : ((r - g) / delta) + 4;
        return ((hue * 60) + 360) % 360;
    }

    private static double HueDistance(double a, double b)
    {
        var d = Math.Abs(a - b) % 360;
        return Math.Min(d, 360 - d);
    }

    private static double RelativeLuminance(string hex)
    {
        Assert.Matches("^#[0-9a-fA-F]{6}$", hex);
        double Channel(int offset)
        {
            var c = int.Parse(hex.AsSpan(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(1)) + (0.7152 * Channel(3)) + (0.0722 * Channel(5));
    }
}
