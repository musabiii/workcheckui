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

        var resources = Application.Current.Resources;
        var mono = font == JetBrainsMono;

        resources["AppFont"] = mono ? JetBrainsMonoFamily : SystemFonts.MessageFontFamily;
        resources["TimerFont"] = mono ? JetBrainsMonoFamily : ConsolasFamily;
    }
}
