using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkCheck.Models;
using WorkCheck.Services;

namespace WorkCheck.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;

    [ObservableProperty] private int _pomodoroMinutes;
    [ObservableProperty] private int _pomodoro2Minutes;
    [ObservableProperty] private int _shortBreakMinutes;
    [ObservableProperty] private int _inactivityMinutes;
    [ObservableProperty] private string _telegramBotToken = "";
    [ObservableProperty] private string _telegramChatId = "";
    [ObservableProperty] private bool _telegramEnabled;

    public event Action<bool>? RequestClose;

    public SettingsViewModel(AppSettings current, SettingsService settingsService)
    {
        _settingsService = settingsService;

        PomodoroMinutes = current.PomodoroMinutes;
        Pomodoro2Minutes = current.Pomodoro2Minutes;
        ShortBreakMinutes = current.ShortBreakMinutes;
        InactivityMinutes = current.InactivityMinutes;
        TelegramBotToken = current.TelegramBotToken;
        TelegramChatId = current.TelegramChatId;
        TelegramEnabled = current.TelegramEnabled;
    }

    [RelayCommand]
    private void Save()
    {
        var settings = new AppSettings
        {
            PomodoroMinutes = PomodoroMinutes,
            Pomodoro2Minutes = Pomodoro2Minutes,
            ShortBreakMinutes = ShortBreakMinutes,
            InactivityMinutes = InactivityMinutes,
            TelegramBotToken = TelegramBotToken,
            TelegramChatId = TelegramChatId,
            TelegramEnabled = TelegramEnabled
        };
        _settingsService.Save(settings);
        RequestClose?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke(false);
    }
}
