using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WorkCheck.Helpers;
using WorkCheck.Models;
using WorkCheck.Services;
using Screen = System.Windows.Forms.Screen;

namespace WorkCheck.Views;

public partial class BreakOverlayWindow : Window
{
    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOACTIVATE = 0x0010;

    private const byte VK_MEDIA_PLAY_PAUSE = 0xB3;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private static readonly Dictionary<NotificationType, string> StripeRoles = new()
    {
        [NotificationType.Pomodoro] = "Yellow",
        [NotificationType.Pomodoro2] = "Red"
    };

    private const string BreakStripeRole = "Blue";

    private readonly List<Window> _secondaryOverlays = [];
    private readonly TimeSpan _breakDuration;
    private DispatcherTimer? _countdownTimer;
    private DispatcherTimer? _lateTimer;
    private DispatcherTimer? _choiceTimer;
    private TimeSpan _remaining;
    private TimeSpan _lateTime;
    private DateTime _modeSelectionStartTime;
    private readonly TimeSpan? _awayBase;
    private readonly TimeSpan? _todayBase;
    private TimeSpan _choiceRemaining;

    private readonly bool _skipPrompt;
    private readonly bool _showChoice;
    private readonly SoundService? _soundService;
    private readonly bool _playSoundOnBreakEnd;
    private bool _breakEndSoundPlayed;
    private bool _breakEndSoundRepeated;
    private bool _countdownFinished;
    private bool _stillAwayReported;

    public bool UserChoseBreak { get; private set; }
    public string SessionDescription { get; set; } = string.Empty;
    public bool? ModeSelected { get; private set; }
    public Action? OnBreakStarted { get; set; }

    public string? ProjectName
    {
        set
        {
            ProjectBlock.Text = $"📁  {value}";
            ProjectChip.Visibility = string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    // The last break countdown started (the default or a 5/15/30/60 minute pick); zero if none
    public TimeSpan BreakLength { get; private set; }

    // Raised when the break countdown runs out
    public Action? OnBreakEnded { get; set; }

    // Raised once if the user is still away StillAwayAfter after the countdown ran out
    public Action? OnStillAway { get; set; }
    public TimeSpan StillAwayAfter { get; set; } = TimeSpan.MaxValue;

    public BreakOverlayWindow(NotificationType type, string title, string message, TimeSpan breakDuration, bool skipPrompt = false, bool showChoice = false, SoundService? soundService = null, bool playSoundOnBreakEnd = false, bool fromWorkMode = false, TimeSpan? awayBase = null, TimeSpan? todayBase = null)
    {
        _awayBase = awayBase;
        _todayBase = todayBase;
        InitializeComponent();

        DataContext = this;
        _breakDuration = breakDuration;
        _skipPrompt = skipPrompt;
        _showChoice = showChoice;
        _soundService = soundService;
        _playSoundOnBreakEnd = playSoundOnBreakEnd;
        if (fromWorkMode)
            _playSoundOnBreakEnd = true;

        TitleBlock.Text = title;
        MessageBlock.Text = message;

        Stripe.Background = AppThemes.Brush(StripeRoles.GetValueOrDefault(type, "Yellow"));

        CreateSecondaryOverlays();

        if (_skipPrompt || _showChoice)
        {
            Loaded += (_, _) =>
            {
                if (_showChoice)
                    SwitchToModeSelection();
                else
                    SwitchToTimerMode();
            };
        }
        else
        {
            StartChoiceTimer();
        }
    }

    private void StartChoiceTimer()
    {
        _choiceRemaining = TimeSpan.FromSeconds(10);
        UpdateChoiceCountdownDisplay();

        _choiceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _choiceTimer.Tick += OnChoiceTick;
        _choiceTimer.Start();
    }

    private void OnChoiceTick(object? sender, EventArgs e)
    {
        _choiceRemaining -= TimeSpan.FromSeconds(1);

        if (_choiceRemaining <= TimeSpan.Zero)
        {
            _choiceTimer?.Stop();
            SwitchToTimerMode();
            return;
        }

        UpdateChoiceCountdownDisplay();
    }

    private void UpdateChoiceCountdownDisplay()
    {
        ChoiceCountdownBlock.Text = $"Перерыв через {_choiceRemaining.Seconds}...";
    }

    private void CreateSecondaryOverlays()
    {
        CoverScreen(this, Screen.PrimaryScreen ?? Screen.AllScreens[0]);

        foreach (var screen in Screen.AllScreens)
        {
            if (screen.Primary) continue;

            var overlay = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = AppThemes.Brush("BaseTranslucent"),
                ResizeMode = ResizeMode.NoResize,
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = false
            };
            CoverScreen(overlay, screen);
            _secondaryOverlays.Add(overlay);
        }
    }

    // Screen bounds are in device pixels while Left/Top/Width/Height are DIPs scaled by the system DPI,
    // so on monitors with a different scale they don't match; place the window in pixels instead
    private static void CoverScreen(Window window, Screen screen)
    {
        var bounds = screen.Bounds;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = bounds.Left;
        window.Top = bounds.Top;
        window.Width = 1;
        window.Height = 1;
        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            SetWindowPos(hwnd, HWND_TOPMOST, bounds.Left, bounds.Top, bounds.Width, bounds.Height, SWP_NOACTIVATE);
        };
    }

    public void ShowWithOverlays()
    {
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
        ModeSelectionPanel.Visibility = Visibility.Collapsed;
        Stripe.Background = AppThemes.Brush(BreakStripeRole);

        StartCountdown(_breakDuration);

        OnBreakStarted?.Invoke();
    }

    private void StartCountdown(TimeSpan duration)
    {
        _countdownTimer?.Stop();
        BreakLength = duration;
        _remaining = duration;
        UpdateCountdownDisplay();

        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += OnCountdownTick;
        _countdownTimer.Start();
    }

    private void OnCountdownTick(object? sender, EventArgs e)
    {
        _remaining -= TimeSpan.FromSeconds(1);

        if (_remaining <= TimeSpan.Zero)
        {
            _countdownTimer?.Stop();
            _countdownFinished = true;
            OnBreakEnded?.Invoke();
            if (_playSoundOnBreakEnd)
            {
                _soundService?.PlayBreakEnd();
                _breakEndSoundPlayed = true;
            }
            SwitchToModeSelection();
            return;
        }

        UpdateCountdownDisplay();
    }

    private void SwitchToModeSelection()
    {
        PromptPanel.Visibility = Visibility.Collapsed;
        TimerPanel.Visibility = Visibility.Collapsed;
        ModeSelectionPanel.Visibility = Visibility.Visible;
        Stripe.Background = AppThemes.Brush(BreakStripeRole);
        QuoteBlock.Text = $"«{Quotes.GetRandom()}»";

        _modeSelectionStartTime = DateTime.Now;
        _lateTime = TimeSpan.Zero;
        UpdateLateTimerDisplay();

        _lateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _lateTimer.Tick += OnLateTimerTick;
        _lateTimer.Start();
    }

    private void OnLateTimerTick(object? sender, EventArgs e)
    {
        _lateTime = DateTime.Now - _modeSelectionStartTime;

        if (_breakEndSoundPlayed && !_breakEndSoundRepeated && _lateTime >= _breakDuration)
        {
            _breakEndSoundRepeated = true;
            _soundService?.PlayBreakEnd();
        }

        if (_countdownFinished && !_stillAwayReported && _lateTime >= StillAwayAfter)
        {
            _stillAwayReported = true;
            OnStillAway?.Invoke();
        }

        UpdateLateTimerDisplay();
    }

    private void UpdateLateTimerDisplay()
    {
        LateTimerBlock.Text = FormatTimer(_lateTime);

        if (_awayBase.HasValue && _todayBase.HasValue)
        {
            TotalAwayValue.Text = TimeFormatter.FormatShort(_awayBase.Value + _lateTime);
            TodayValue.Text = TimeFormatter.FormatShort(_todayBase.Value);
            GrandTotalValue.Text = TimeFormatter.FormatShort(_todayBase.Value + _awayBase.Value + _lateTime);
            TotalsGrid.Visibility = Visibility.Visible;
        }
    }

    private void UpdateCountdownDisplay()
    {
        CountdownBlock.Text = FormatTimer(_remaining);
    }

    private static string FormatTimer(TimeSpan time) =>
        time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");

    private void Dismiss(bool choseBreak)
    {
        _countdownTimer?.Stop();
        UserChoseBreak = choseBreak;
        ModeSelected = null;
        CloseAll();
        DialogResult = choseBreak;
    }

    private void DismissWithMode(bool driftMode)
    {
        _countdownTimer?.Stop();
        _lateTimer?.Stop();
        UserChoseBreak = true;
        ModeSelected = driftMode;
        CloseAll();
        DialogResult = true;
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
        _choiceTimer?.Stop();
        SwitchToTimerMode();
    }

    private void OnContinueClick(object sender, RoutedEventArgs e)
    {
        _choiceTimer?.Stop();
        Dismiss(choseBreak: false);
    }

    private void OnCancelBreakClick(object sender, RoutedEventArgs e)
    {
        Dismiss(choseBreak: false);
    }

    private void OnDriftClick(object sender, RoutedEventArgs e)
    {
        DismissWithMode(driftMode: true);
    }

    private void OnWorkClick(object sender, RoutedEventArgs e)
    {
        DismissWithMode(driftMode: false);
    }

    private void OnPlayPauseClick(object sender, RoutedEventArgs e)
    {
        SendMediaPause();
    }

    private void OnSet5Min(object sender, RoutedEventArgs e) => StartCountdown(TimeSpan.FromMinutes(5));

    private void OnSet15Min(object sender, RoutedEventArgs e) => StartCountdown(TimeSpan.FromMinutes(15));

    private void OnSet30Min(object sender, RoutedEventArgs e) => StartCountdown(TimeSpan.FromMinutes(30));

    private void OnSet1Hour(object sender, RoutedEventArgs e) => StartCountdown(TimeSpan.FromHours(1));

    private void OnAdd5Min(object sender, RoutedEventArgs e) => AddTime(TimeSpan.FromMinutes(5));

    private void OnAdd10Min(object sender, RoutedEventArgs e) => AddTime(TimeSpan.FromMinutes(10));

    private void OnAdd30Min(object sender, RoutedEventArgs e) => AddTime(TimeSpan.FromMinutes(30));

    private void AddTime(TimeSpan amount)
    {
        _remaining += amount;
        UpdateCountdownDisplay();
    }
}
