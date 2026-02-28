using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WorkCheck.Models;
using Screen = System.Windows.Forms.Screen;
using Color = System.Windows.Media.Color;

namespace WorkCheck.Views;

public partial class BreakOverlayWindow : Window
{
    private static readonly Dictionary<NotificationType, Color> StripeColors = new()
    {
        [NotificationType.Pomodoro] = Color.FromRgb(0xF9, 0xE2, 0xAF),
        [NotificationType.Pomodoro2] = Color.FromRgb(0xF3, 0x8B, 0xA8)
    };

    private readonly List<Window> _secondaryOverlays = [];

    public bool UserChoseBreak { get; private set; }

    public BreakOverlayWindow(NotificationType type, string title, string message)
    {
        InitializeComponent();

        TitleBlock.Text = title;
        MessageBlock.Text = message;

        var color = StripeColors.GetValueOrDefault(type, Color.FromRgb(0xF9, 0xE2, 0xAF));
        Stripe.Background = new SolidColorBrush(color);

        CreateSecondaryOverlays();
    }

    private void CreateSecondaryOverlays()
    {
        foreach (var screen in Screen.AllScreens)
        {
            if (screen.Primary) continue;

            var overlay = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xDD, 0x1E, 0x1E, 0x2E)),
                ResizeMode = ResizeMode.NoResize,
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = false,
                Left = screen.Bounds.Left,
                Top = screen.Bounds.Top,
                Width = screen.Bounds.Width,
                Height = screen.Bounds.Height
            };
            _secondaryOverlays.Add(overlay);
        }
    }

    public void ShowWithOverlays()
    {
        foreach (var overlay in _secondaryOverlays)
            overlay.Show();

        ShowDialog();
    }

    public void PlayFadeIn()
    {
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        OuterRoot.BeginAnimation(OpacityProperty, fadeIn);
    }

    private void CloseAll()
    {
        foreach (var overlay in _secondaryOverlays)
        {
            try { overlay.Close(); }
            catch { /* already closed */ }
        }
        _secondaryOverlays.Clear();
    }

    private void OnBreakClick(object sender, RoutedEventArgs e)
    {
        UserChoseBreak = true;
        CloseAll();
        DialogResult = true;
    }

    private void OnContinueClick(object sender, RoutedEventArgs e)
    {
        UserChoseBreak = false;
        CloseAll();
        DialogResult = false;
    }
}
