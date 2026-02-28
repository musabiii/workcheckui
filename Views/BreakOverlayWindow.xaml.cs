using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WorkCheck.Models;
using Screen = System.Windows.Forms.Screen;
using Color = System.Windows.Media.Color;

namespace WorkCheck.Views;

public partial class BreakOverlayWindow : Window
{
    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private const byte VK_MEDIA_PLAY_PAUSE = 0xB3;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private static readonly Dictionary<NotificationType, Color> StripeColors = new()
    {
        [NotificationType.Pomodoro] = Color.FromRgb(0xF9, 0xE2, 0xAF),
        [NotificationType.Pomodoro2] = Color.FromRgb(0xF3, 0x8B, 0xA8)
    };

    private static readonly Color BreakStripeColor = Color.FromRgb(0x89, 0xB4, 0xFA);

    private readonly List<Window> _secondaryOverlays = [];
    private readonly TimeSpan _breakDuration;
    private DispatcherTimer? _countdownTimer;
    private TimeSpan _remaining;

    public bool UserChoseBreak { get; private set; }
    public Action? OnBreakStarted { get; set; }

    public BreakOverlayWindow(NotificationType type, string title, string message, TimeSpan breakDuration)
    {
        InitializeComponent();

        _breakDuration = breakDuration;

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
                Background = new SolidColorBrush(Color.FromArgb(0xDD, 0x1E, 0x1E, 0x2E)),
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
        SendMediaPause();

        foreach (var overlay in _secondaryOverlays)
            overlay.Show();

        ShowDialog();
    }

    private static void SendMediaPause()
    {
        keybd_event(VK_MEDIA_PLAY_PAUSE, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
        keybd_event(VK_MEDIA_PLAY_PAUSE, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    public void PlayFadeIn()
    {
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        OuterRoot.BeginAnimation(OpacityProperty, fadeIn);
    }

    private void SwitchToTimerMode()
    {
        PromptPanel.Visibility = Visibility.Collapsed;
        TimerPanel.Visibility = Visibility.Visible;
        Stripe.Background = new SolidColorBrush(BreakStripeColor);

        _remaining = _breakDuration;
        UpdateCountdownDisplay();

        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += OnCountdownTick;
        _countdownTimer.Start();

        OnBreakStarted?.Invoke();
    }

    private void OnCountdownTick(object? sender, EventArgs e)
    {
        _remaining -= TimeSpan.FromSeconds(1);

        if (_remaining <= TimeSpan.Zero)
        {
            _countdownTimer?.Stop();
            Dismiss(choseBreak: true);
            return;
        }

        UpdateCountdownDisplay();
    }

    private void UpdateCountdownDisplay()
    {
        CountdownBlock.Text = _remaining.ToString(@"m\:ss");
    }

    private void Dismiss(bool choseBreak)
    {
        _countdownTimer?.Stop();
        UserChoseBreak = choseBreak;
        CloseAll();
        DialogResult = choseBreak;
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
        SwitchToTimerMode();
    }

    private void OnContinueClick(object sender, RoutedEventArgs e)
    {
        Dismiss(choseBreak: false);
    }

    private void OnCancelBreakClick(object sender, RoutedEventArgs e)
    {
        Dismiss(choseBreak: false);
    }
}
