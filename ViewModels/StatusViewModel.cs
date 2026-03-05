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
    private readonly DataService _dataService;
    private AppSettings _settings;
    private readonly DispatcherTimer _timer;

    [ObservableProperty] private bool _isWorkMode;
    [ObservableProperty] private string _currentSessionText = "0 мин";
    [ObservableProperty] private string _awayTimeText = "0 мин";
    [ObservableProperty] private string _todayWorkedText = "0 мин";
    [ObservableProperty] private string _statusText = "Дрейфую";
    [ObservableProperty] private Brush _statusBrush = DriftingGrayBrush;
    [ObservableProperty] private Brush _sessionBrush = NormalTextBrush;
    [ObservableProperty] private Brush _windowBgBrush = DriftingBgBrush;
    [ObservableProperty] private string _modeButtonText = "▶  В работе";
    [ObservableProperty] private string _awayLabel = "💤  Вне компьютера:";
    [ObservableProperty] private string _sessionIcon = "🖥";
    [ObservableProperty] private string _sessionLabel = "  Текущая сессия:";

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
        DataService dataService,
        AppSettings settings,
        TrayIconService trayIcon)
    {
        _tracker = tracker;
        _notifications = notifications;
        _telegram = telegram;
        _trayIcon = trayIcon;
        _settingsService = settingsService;
        _dataService = dataService;
        _settings = settings;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTick;
        _timer.Start();
        
        UpdateTodayWorkedText();
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

        foreach (var req in requests)
        {
            if (req.Type is NotificationType.Pomodoro or NotificationType.Pomodoro2)
            {
                var overlayShownAt = DateTime.Now;

                var (choseBreak, description, modeSelected) = _notifications.ShowBreakOverlay(
                    req.Type, req.Title, req.Message,
                    _settings.ShortBreakTime,
                    onBreakStarted: () => _tracker.IsPaused = true);
                _tracker.IsPaused = false;

                // Корректируем время: пока висел оверлей (ожидание + возможный перерыв),
                // пользователь не работал — это не должно считаться рабочим временем
                _tracker.AccountOverlayIdle(overlayShownAt, description);

                // Если выбран режим после перерыва, переключаем
                if (modeSelected.HasValue)
                {
                    if (modeSelected.Value == false)
                    {
                        // Продолжить работать
                        if (!IsWorkMode)
                            ToggleMode();
                    }
                    else
                    {
                        // Дрейфовать
                        if (IsWorkMode)
                            ToggleMode();
                    }
                }
            }
            else if (IsWorkMode)
            {
                _notifications.Show(req.Type, req.Title, req.Message, req.SecondaryMessage, req.Quote);
            }

            if (IsWorkMode && req.SendTelegram && !string.IsNullOrEmpty(req.TelegramText))
                _ = _telegram.SendAsync(req.TelegramText, req.SilentTelegram);
        }
    }

    private void UpdateDisplay()
    {
        var session = _tracker.CurrentSession;
        CurrentSessionText = TimeFormatter.FormatShort(session);
        
        var awayTime = _tracker.GetAwayTimeFromDatabase() + _tracker.DisplayAwayTime;
        AwayTimeText = TimeFormatter.FormatShort(awayTime);

        var sessions = _tracker.CompletedSessions;
        SessionLabel = sessions > 0
            ? $"  Текущая сессия ({sessions}):"
            : "  Текущая сессия:";

        UpdateTodayWorkedText(session);

        if (IsWorkMode)
        {
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

    private void UpdateTodayWorkedText(TimeSpan currentSession = default)
    {
        var todayTotal = _dataService.GetTotalWorkTimeByDate(DateTime.Today, IsWorkMode);
        var totalWithCurrent = todayTotal + currentSession;
        TodayWorkedText = TimeFormatter.FormatShort(totalWithCurrent);
    }

    [RelayCommand]
    private void ManualBreak()
    {
        var overlayShownAt = DateTime.Now;

        var (_, description, modeSelected) = _notifications.ShowBreakOverlay(
            NotificationType.Pomodoro,
            "☕  Ручной перерыв",
            $"Поработали {TimeFormatter.FormatShort(_tracker.CurrentSession)}",
            _settings.ShortBreakTime,
            onBreakStarted: () => _tracker.IsPaused = true,
            skipPrompt: true);
        _tracker.IsPaused = false;

        _tracker.AccountOverlayIdle(overlayShownAt, description);

        if (modeSelected.HasValue)
        {
            if (modeSelected.Value == false)
            {
                if (!IsWorkMode)
                    ToggleMode();
            }
            else
            {
                if (IsWorkMode)
                    ToggleMode();
            }
        }
    }

    [RelayCommand]
    private void Reset()
    {
        var session = _tracker.CurrentSession;
        
        _tracker.Reset();
        CurrentSessionText = "0 мин";
        AwayTimeText = "0 мин";
        UpdateTodayWorkedText();
    }

    [RelayCommand]
    private void ToggleMode()
    {
        // Сохраняем текущую накопленную сессию и период неактивности в БД перед переключением
        _tracker.SaveCurrentSession();
        _tracker.SaveCurrentAwayPeriod();
        
        IsWorkMode = !IsWorkMode;
        _tracker.IsWorkMode = IsWorkMode;
        _tracker.Reset();

        TodayWorkedText = TimeFormatter.FormatShort(_dataService.GetTotalWorkTimeByDate(DateTime.Today, IsWorkMode));
        CurrentSessionText = "0 мин";
        AwayTimeText = "0 мин";

        if (IsWorkMode)
        {
            ModeButtonText = "⏸  Дрейфую";
            WindowBgBrush = WorkingBgBrush;
            AwayLabel = "☕  Вне работы:";
            SessionIcon = "🟢";
        }
        else
        {
            ModeButtonText = "▶  В работе";
            WindowBgBrush = DriftingBgBrush;
            AwayLabel = "💤  Вне компьютера:";
            SessionIcon = "🖥";
        }
    }

    [RelayCommand]
    private void OpenStatistics()
    {
        var window = new StatisticsWindow(_dataService) { Owner = Application.Current.MainWindow };
        window.ShowDialog();
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
