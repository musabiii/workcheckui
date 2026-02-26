using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
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
    private readonly SettingsService _settingsService;
    private AppSettings _settings;
    private readonly DispatcherTimer _timer;

    [ObservableProperty] private string _currentSessionText = "0 мин";
    [ObservableProperty] private string _workedTimeText = "0 мин";
    [ObservableProperty] private string _awayTimeText = "0 мин";
    [ObservableProperty] private string _statusText = "Активен";
    [ObservableProperty] private Brush _statusBrush = new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1));
    [ObservableProperty] private Brush _sessionBrush = new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1));

    private static readonly Brush ActiveBrush = new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1));
    private static readonly Brush ShortBreakBrush = new SolidColorBrush(Color.FromRgb(0xF9, 0xE2, 0xAF));
    private static readonly Brush InactiveBrush = new SolidColorBrush(Color.FromRgb(0xF3, 0x8B, 0xA8));

    static StatusViewModel()
    {
        ActiveBrush.Freeze();
        ShortBreakBrush.Freeze();
        InactiveBrush.Freeze();
    }

    public StatusViewModel(
        ActivityTracker tracker,
        NotificationService notifications,
        TelegramService telegram,
        SettingsService settingsService,
        AppSettings settings)
    {
        _tracker = tracker;
        _notifications = notifications;
        _telegram = telegram;
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

        foreach (var req in requests)
        {
            _notifications.Show(req.Type, req.Title, req.Message, req.SecondaryMessage, req.Quote);

            if (req.SendTelegram && !string.IsNullOrEmpty(req.TelegramText))
                _ = _telegram.SendAsync(req.TelegramText, req.SilentTelegram);
        }
    }

    private void UpdateDisplay()
    {
        var session = _tracker.CurrentSession;
        CurrentSessionText = TimeFormatter.FormatShort(session);
        WorkedTimeText = TimeFormatter.FormatShort(_tracker.DisplayWorkedTime);
        AwayTimeText = TimeFormatter.FormatShort(_tracker.DisplayAwayTime);

        if (session >= _settings.Pomodoro2Time)
            SessionBrush = InactiveBrush;
        else if (session >= _settings.PomodoroTime)
            SessionBrush = ShortBreakBrush;
        else
            SessionBrush = ActiveBrush;

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
        Cleanup();
        Application.Current.Shutdown();
    }

    public void Cleanup()
    {
        _timer.Stop();
        _tracker.StopHooks();
        _notifications.CloseAll();
        Debug.WriteLine($"[WorkCheck] {_tracker.GetSummary()}");
    }
}
