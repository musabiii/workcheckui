using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkCheck.Helpers;
using WorkCheck.Models;
using WorkCheck.Services;
using WorkCheck.Views;

namespace WorkCheck.ViewModels;

public partial class StatusViewModel : ObservableObject
{
    private readonly ActivityTracker _tracker;
    private readonly NotificationService _notifications;
    private readonly TelegramService _telegram;
    private readonly TrayIconService _trayIcon;
    private readonly SettingsService _settingsService;
    private AppSettings _settings;
    private readonly DispatcherTimer _timer;

    [ObservableProperty] private bool _isWorkMode;
    [ObservableProperty] private string _currentSessionText = "0 мин";
    [ObservableProperty] private string _workedTimeText = "0 мин";
    [ObservableProperty] private string _awayTimeText = "0 мин";
    [ObservableProperty] private string _statusText = "Дрейфую";
    [ObservableProperty] private Brush _statusBrush = DriftingGrayBrush;
    [ObservableProperty] private Brush _sessionBrush = NormalTextBrush;
    [ObservableProperty] private Brush _windowBgBrush = DriftingBgBrush;
    [ObservableProperty] private string _modeButtonText = "▶  В работе";
    [ObservableProperty] private string _awayLabel = "💤  Вне компьютера:";
    [ObservableProperty] private string _sessionIcon = "🖥";
    [ObservableProperty] private Visibility _workedRowVisibility = Visibility.Collapsed;

    private static readonly Brush ActiveBrush = new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1));
    private static readonly Brush ShortBreakBrush = new SolidColorBrush(Color.FromRgb(0xF9, 0xE2, 0xAF));
    private static readonly Brush InactiveBrush = new SolidColorBrush(Color.FromRgb(0xF3, 0x8B, 0xA8));
    private static readonly Brush NormalTextBrush = new SolidColorBrush(Color.FromRgb(0xBA, 0xC2, 0xDE));
    private static readonly Brush DriftingGrayBrush = new SolidColorBrush(Color.FromRgb(0x6C, 0x70, 0x86));
    private static readonly Brush DriftingBgBrush = new SolidColorBrush(Color.FromRgb(0x31, 0x31, 0x31));
    private static readonly Brush WorkingBgBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x2E));

    static StatusViewModel()
    {
        ActiveBrush.Freeze();
        ShortBreakBrush.Freeze();
        InactiveBrush.Freeze();
        NormalTextBrush.Freeze();
        DriftingGrayBrush.Freeze();
        DriftingBgBrush.Freeze();
        WorkingBgBrush.Freeze();
    }

    public StatusViewModel(
        ActivityTracker tracker,
        NotificationService notifications,
        TelegramService telegram,
        SettingsService settingsService,
        AppSettings settings,
        TrayIconService trayIcon)
    {
        _tracker = tracker;
        _notifications = notifications;
        _telegram = telegram;
        _trayIcon = trayIcon;
        _settingsService = settingsService;
        _settings = settings;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        try
        {
            ProcessNotifications();
            UpdateDisplay();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[StatusVM] Ошибка в OnTick: {ex}");
        }
    }

    private void ProcessNotifications()
    {
        var requests = _tracker.Tick();

        if (!IsWorkMode) return;

        foreach (var req in requests)
        {
            if (req.Type is NotificationType.Pomodoro or NotificationType.Pomodoro2)
            {
                _notifications.ShowBreakOverlay(
                    req.Type, req.Title, req.Message,
                    _settings.ShortBreakTime,
                    onBreakStarted: () => _tracker.IsPaused = true);
                _tracker.IsPaused = false;
            }
            else
                _notifications.Show(req.Type, req.Title, req.Message, req.SecondaryMessage, req.Quote);

            if (req.SendTelegram && !string.IsNullOrEmpty(req.TelegramText))
                _ = _telegram.SendAsync(req.TelegramText, req.SilentTelegram);
        }
    }

    private void UpdateDisplay()
    {
        var session = _tracker.CurrentSession;
        CurrentSessionText = TimeFormatter.FormatShort(session);
        AwayTimeText = TimeFormatter.FormatShort(_tracker.DisplayAwayTime);

        if (IsWorkMode)
        {
            WorkedTimeText = TimeFormatter.FormatShort(_tracker.DisplayWorkedTime);

            if (session >= _settings.Pomodoro2Time)
                SessionBrush = InactiveBrush;
            else if (session >= _settings.PomodoroTime)
                SessionBrush = ShortBreakBrush;
            else
                SessionBrush = ActiveBrush;

            _trayIcon.Update((int)session.TotalMinutes, session,
                _settings.PomodoroTime, _settings.Pomodoro2Time, isDrifting: false);

            if (_tracker.UserActive && !_tracker.UserShortBreak)
            {
                StatusText = "Активен";
                StatusBrush = ActiveBrush;
            }
            else if (_tracker.UserShortBreak)
            {
                StatusText = "Короткий перерыв";
                StatusBrush = ShortBreakBrush;
            }
            else
            {
                StatusText = "Неактивен";
                StatusBrush = InactiveBrush;
            }
        }
        else
        {
            SessionBrush = NormalTextBrush;
            StatusText = "Дрейфую";
            StatusBrush = DriftingGrayBrush;

            _trayIcon.Update((int)session.TotalMinutes, session,
                _settings.PomodoroTime, _settings.Pomodoro2Time, isDrifting: true);
        }
    }

    [RelayCommand]
    private void Reset()
    {
        _tracker.Reset();
        CurrentSessionText = "0 мин";
        WorkedTimeText = "0 мин";
        AwayTimeText = "0 мин";
    }

    [RelayCommand]
    private void ToggleMode()
    {
        IsWorkMode = !IsWorkMode;
        _tracker.Reset();

        if (IsWorkMode)
        {
            ModeButtonText = "⏸  Дрейфую";
            WindowBgBrush = WorkingBgBrush;
            WorkedRowVisibility = Visibility.Visible;
            AwayLabel = "☕  Вне работы:";
            SessionIcon = "🟢";
        }
        else
        {
            ModeButtonText = "▶  В работе";
            WindowBgBrush = DriftingBgBrush;
            WorkedRowVisibility = Visibility.Collapsed;
            AwayLabel = "💤  Вне компьютера:";
            SessionIcon = "🖥";
        }
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var vm = new SettingsViewModel(_settings, _settingsService);
        var window = new SettingsWindow { DataContext = vm };
        vm.RequestClose += result => { window.DialogResult = result; };

        if (window.ShowDialog() == true)
        {
            _settings = _settingsService.Load();
            _tracker.ApplySettings(_settings);
            _telegram.Settings = _settings;
        }
    }

    [RelayCommand]
    private void CloseApp()
    {
        Application.Current.MainWindow?.Hide();
    }

    public void Cleanup()
    {
        _timer.Stop();
        _tracker.StopHooks();
        _notifications.CloseAll();
        _trayIcon.Dispose();
        Debug.WriteLine($"[WorkCheck] {_tracker.GetSummary()}");
    }
}
