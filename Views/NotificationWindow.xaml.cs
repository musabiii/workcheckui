using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WorkCheck.Models;
using Color = System.Windows.Media.Color;

namespace WorkCheck.Views;

public partial class NotificationWindow : Window
{
    private readonly DispatcherTimer _autoCloseTimer;
    private bool _isClosing;

    private static readonly Dictionary<NotificationType, Color> StripeColors = new()
    {
        [NotificationType.Welcome] = Color.FromRgb(0xA6, 0xE3, 0xA1),
        [NotificationType.Pomodoro] = Color.FromRgb(0xF9, 0xE2, 0xAF),
        [NotificationType.Pomodoro2] = Color.FromRgb(0xF3, 0x8B, 0xA8),
        [NotificationType.ShortBreak] = Color.FromRgb(0x89, 0xB4, 0xFA),
        [NotificationType.Inactivity] = Color.FromRgb(0xF3, 0x8B, 0xA8)
    };

    public NotificationWindow(NotificationType type, string title, string message,
        string? secondary, string? quote)
    {
        InitializeComponent();

        TitleBlock.Text = title;
        MessageBlock.Text = message;

        if (!string.IsNullOrWhiteSpace(secondary))
        {
            SecondaryBlock.Text = secondary;
            SecondaryBlock.Visibility = Visibility.Visible;
        }

        if (!string.IsNullOrWhiteSpace(quote))
        {
            QuoteBlock.Text = $"«{quote}»";
            QuoteBlock.Visibility = Visibility.Visible;
        }

        var color = StripeColors.GetValueOrDefault(type, Color.FromRgb(0x89, 0xB4, 0xFA));
        Stripe.Background = new SolidColorBrush(color);

        if (type == NotificationType.Inactivity)
        {
            TitleBlock.FontSize = 16;
            MessageBlock.FontSize = 14;
        }

        OuterRoot.Opacity = 0;

        _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _autoCloseTimer.Tick += (_, _) =>
        {
            _autoCloseTimer.Stop();
            AnimateClose();
        };
        _autoCloseTimer.Start();
    }

    public void PlayFadeIn()
    {
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        var slideIn = new DoubleAnimation(20, 0, TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        OuterRoot.BeginAnimation(OpacityProperty, fadeIn);
        SlideTransform.BeginAnimation(TranslateTransform.YProperty, slideIn);
    }

    public void AnimateClose()
    {
        if (_isClosing) return;
        _isClosing = true;

        _autoCloseTimer.Stop();

        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(500))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        fadeOut.Completed += (_, _) => SafeClose();

        OuterRoot.BeginAnimation(OpacityProperty, fadeOut);
    }

    public void ForceClose()
    {
        _isClosing = true;
        _autoCloseTimer.Stop();
        OuterRoot.BeginAnimation(OpacityProperty, null);
        SafeClose();
    }

    private void SafeClose()
    {
        try { Close(); }
        catch (InvalidOperationException) { }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        AnimateClose();
    }
}
