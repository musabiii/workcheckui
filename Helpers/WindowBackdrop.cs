using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using Border = System.Windows.Controls.Border;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace WorkCheck.Helpers;

// Windows 11 draws a system backdrop (blurred Acrylic, or Mica tinted by the wallpaper) only under regular
// windows, not under layered ones (AllowsTransparency). For themes with a backdrop a dialog is switched to
// WindowChrome before it is shown; Windows then also draws its rounded corners and shadow, so the window's
// own ones are dropped.
public static class WindowBackdrop
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmwcpRound = 2;
    private const int DwmsbtMainWindow = 2;
    private const int DwmsbtTransientWindow = 3;
    private const uint DwmwaColorNone = 0xFFFFFFFE;
    private const int GwlStyle = -16;
    private const int WsSysMenu = 0x00080000;
    private const int WsMinimizeBox = 0x00020000;
    private const int WsMaximizeBox = 0x00010000;

    // DWMWA_SYSTEMBACKDROP_TYPE exists from Windows 11 22H2
    private static bool IsSupported => Environment.OSVersion.Version.Build >= 22621;

    // Call after InitializeComponent and before the window is shown
    public static void Apply(Window window)
    {
        var backdrop = AppThemes.BackdropOf(AppThemes.Current);
        if (backdrop == Backdrop.None || !IsSupported) return;

        window.AllowsTransparency = false;
        window.WindowStyle = WindowStyle.SingleBorderWindow;
        window.Background = Brushes.Transparent;
        if (window.ResizeMode == ResizeMode.CanResizeWithGrip)
            window.ResizeMode = ResizeMode.CanResize;

        // The transparent space reserved for the window's own shadow is not needed any more
        var space = 2 * AppThemes.WindowShadowSpace;
        if (!double.IsNaN(window.Width)) window.Width -= space;
        if (!double.IsNaN(window.Height)) window.Height -= space;
        if (window.MinWidth > 0) window.MinWidth -= space;
        if (window.MinHeight > 0) window.MinHeight -= space;

        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = 0,
            GlassFrameThickness = new Thickness(-1),
            ResizeBorderThickness = new Thickness(6),
            UseAeroCaptionButtons = false
        });

        if (window.Content is Border root)
        {
            root.Margin = new Thickness(0);
            root.Effect = null;
            root.CornerRadius = new CornerRadius(8);
            root.SetResourceReference(Border.BackgroundProperty, "BackdropTintBrush");
        }

        window.SourceInitialized += (_, _) => EnableBackdrop(window, backdrop);
    }

    private static void EnableBackdrop(Window window, Backdrop backdrop)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target)
                target.BackgroundColor = Colors.Transparent;

            // With the frame extended over the whole window Windows would draw its own caption buttons
            SetWindowLong(hwnd, GwlStyle, GetWindowLong(hwnd, GwlStyle) & ~(WsSysMenu | WsMinimizeBox | WsMaximizeBox));

            var dark = Luminance(AppThemes.Color("Base")) < 0.5 ? 1 : 0;
            SetInt(hwnd, DwmwaUseImmersiveDarkMode, dark);
            SetInt(hwnd, DwmwaWindowCornerPreference, DwmwcpRound);
            SetInt(hwnd, DwmwaSystemBackdropType, backdrop == Backdrop.Mica ? DwmsbtMainWindow : DwmsbtTransientWindow);
            // The theme draws its own light edge, so the system one is turned off
            var none = DwmwaColorNone;
            DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref none, sizeof(uint));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            Debug.WriteLine($"[WindowBackdrop] {ex.Message}");
        }
    }

    private static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;

    private static void SetInt(IntPtr hwnd, int attribute, int value) =>
        DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref uint value, int size);
}
