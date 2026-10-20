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
        [SolarizedLight] = new Uri("Themes/SolarizedLight.xaml", UriKind.Relative)
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
        [SolarizedLight] = AppFonts.JetBrainsMono
    };

    public static bool IsKnown(string theme) => Sources.ContainsKey(theme);

    public static string FontOf(string theme) => Fonts.GetValueOrDefault(theme, AppFonts.System);

    public static void Apply(string theme)
    {
        var source = Sources.GetValueOrDefault(theme, Sources[Mocha]);
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
