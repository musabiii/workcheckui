using Application = System.Windows.Application;
using FontFamily = System.Windows.Media.FontFamily;
using SystemFonts = System.Windows.SystemFonts;

namespace WorkCheck.Helpers;

public static class AppFonts
{
    public const string FromTheme = "Theme";
    public const string System = "System";
    public const string JetBrainsMono = "JetBrains Mono";

    private static readonly FontFamily JetBrainsMonoFamily =
        new(new Uri("pack://application:,,,/"), "./files/fonts/#JetBrains Mono");

    private static readonly FontFamily ConsolasFamily = new("Consolas");

    public static void Apply(string font, string theme)
    {
        if (font == FromTheme)
            font = AppThemes.FontOf(theme);

        // Besides the two picker options a theme may name any installed font family
        var resources = Application.Current.Resources;
        (resources["AppFont"], resources["TimerFont"]) = font switch
        {
            JetBrainsMono => (JetBrainsMonoFamily, JetBrainsMonoFamily),
            System => (SystemFonts.MessageFontFamily, ConsolasFamily),
            _ => (new FontFamily(font), new FontFamily(font))
        };
    }
}
