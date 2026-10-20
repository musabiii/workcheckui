using System.Windows;
using System.Windows.Media;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace WorkCheck.Helpers;

public static class AppThemes
{
    public const string Mocha = "Catppuccin Mocha";
    public const string GitHubDark = "GitHub Dark";
    public const string Darcula = "Darcula";
    public const string Gruvbox = "Gruvbox Dark";
    public const string TokyoNight = "Tokyo Night";
    public const string Dracula = "Dracula";
    public const string Nord = "Nord";
    public const string Latte = "Catppuccin Latte";
    public const string GitHubLight = "GitHub Light";
    public const string SolarizedLight = "Solarized Light";
    public const string Glass = "Liquid Glass";
    public const string Synthwave = "Synthwave '84";
    public const string Cyberpunk = "Cyberpunk";
    public const string Terminal = "Terminal";
    public const string FluentDark = "Fluent Dark";
    public const string FluentLight = "Fluent Light";
    public const string GlassDark = "Liquid Glass Dark";

    // Themes whose dialogs get a system backdrop behind them (see WindowBackdrop)
    private static readonly Dictionary<string, Backdrop> Backdrops = new()
    {
        [Glass] = Backdrop.Acrylic,
        [GlassDark] = Backdrop.Acrylic,
        [FluentDark] = Backdrop.Mica,
        [FluentLight] = Backdrop.Mica
    };

    public static string Current { get; private set; } = Mocha;

    // Transparent space around windows for the shadow, same as WindowShadowSpace in Themes/Tokens.xaml
    public const double WindowShadowSpace = 16;

    private static readonly Dictionary<string, Uri> Sources = new()
    {
        [Mocha] = new Uri("Themes/Mocha.xaml", UriKind.Relative),
        [GitHubDark] = new Uri("Themes/GitHubDark.xaml", UriKind.Relative),
        [Darcula] = new Uri("Themes/Darcula.xaml", UriKind.Relative),
        [Gruvbox] = new Uri("Themes/Gruvbox.xaml", UriKind.Relative),
        [TokyoNight] = new Uri("Themes/TokyoNight.xaml", UriKind.Relative),
        [Dracula] = new Uri("Themes/Dracula.xaml", UriKind.Relative),
        [Nord] = new Uri("Themes/Nord.xaml", UriKind.Relative),
        [Latte] = new Uri("Themes/Latte.xaml", UriKind.Relative),
        [GitHubLight] = new Uri("Themes/GitHubLight.xaml", UriKind.Relative),
        [SolarizedLight] = new Uri("Themes/SolarizedLight.xaml", UriKind.Relative),
        [Glass] = new Uri("Themes/Glass.xaml", UriKind.Relative),
        [Synthwave] = new Uri("Themes/Synthwave.xaml", UriKind.Relative),
        [Cyberpunk] = new Uri("Themes/Cyberpunk.xaml", UriKind.Relative),
        [Terminal] = new Uri("Themes/Terminal.xaml", UriKind.Relative),
        [FluentDark] = new Uri("Themes/FluentDark.xaml", UriKind.Relative),
        [FluentLight] = new Uri("Themes/FluentLight.xaml", UriKind.Relative),
        [GlassDark] = new Uri("Themes/GlassDark.xaml", UriKind.Relative)
    };

    // Font used by the "from theme" font option. GitHub's own Mona Sans has no Cyrillic, so GitHub Dark
    // uses the system font, which is also what github.com renders with on Windows.
    private static readonly Dictionary<string, string> Fonts = new()
    {
        [Mocha] = AppFonts.System,
        [GitHubDark] = AppFonts.System,
        [Darcula] = AppFonts.JetBrainsMono,
        [Gruvbox] = AppFonts.JetBrainsMono,
        [TokyoNight] = AppFonts.JetBrainsMono,
        [Dracula] = AppFonts.System,
        [Nord] = AppFonts.System,
        [Latte] = AppFonts.System,
        [GitHubLight] = AppFonts.System,
        [SolarizedLight] = AppFonts.JetBrainsMono,
        [Glass] = AppFonts.System,
        [Synthwave] = AppFonts.System,
        [Cyberpunk] = AppFonts.JetBrainsMono,
        [Terminal] = AppFonts.JetBrainsMono,
        [FluentDark] = AppFonts.System,
        [FluentLight] = AppFonts.System,
        [GlassDark] = AppFonts.System
    };

    public static bool IsKnown(string theme) => Sources.ContainsKey(theme);

    public static Backdrop BackdropOf(string theme) => Backdrops.GetValueOrDefault(theme, Backdrop.None);

    public static string FontOf(string theme) => Fonts.GetValueOrDefault(theme, AppFonts.System);

    public static void Apply(string theme)
    {
        Current = IsKnown(theme) ? theme : Mocha;
        var source = Sources[Current];
        var merged = Application.Current.Resources.MergedDictionaries;

        var current = merged.FirstOrDefault(d => d.Source != null && Sources.ContainsValue(d.Source));
        if (current?.Source == source) return;

        var dictionary = new ResourceDictionary { Source = source };
        if (current != null)
            merged[merged.IndexOf(current)] = dictionary;
        else
            merged.Add(dictionary);
    }

    // For code that draws with theme colors; looked up on use so it follows theme changes
    public static Brush Brush(string role) => (Brush)Application.Current.Resources[role + "Brush"];

    public static Color Color(string role) => ((SolidColorBrush)Brush(role)).Color;
}

public enum Backdrop { None, Acrylic, Mica }
