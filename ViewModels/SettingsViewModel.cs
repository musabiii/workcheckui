using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkCheck.Helpers;
using WorkCheck.Models;
using WorkCheck.Services;

namespace WorkCheck.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private readonly TelegramService _telegramService;

    [ObservableProperty] private int _pomodoroMinutes;
    [ObservableProperty] private int _pomodoro2Minutes;
    [ObservableProperty] private int _shortBreakMinutes;
    [ObservableProperty] private int _inactivityMinutes;
    [ObservableProperty] private string _telegramBotToken = "";
    [ObservableProperty] private string _telegramChatId = "";
    [ObservableProperty] private string _proxyUrl = "";
    [ObservableProperty] private bool _telegramEnabled;
    [ObservableProperty] private bool _soundEnabled;
    [ObservableProperty] private string _testStatus = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsThemeFont), nameof(IsSystemFont), nameof(IsJetBrainsMonoFont))]
    private string _font = AppFonts.FromTheme;

    public bool IsThemeFont
    {
        get => Font != AppFonts.System && Font != AppFonts.JetBrainsMono;
        set { if (value) Font = AppFonts.FromTheme; }
    }

    public bool IsSystemFont
    {
        get => Font == AppFonts.System;
        set { if (value) Font = AppFonts.System; }
    }

    public bool IsJetBrainsMonoFont
    {
        get => Font == AppFonts.JetBrainsMono;
        set { if (value) Font = AppFonts.JetBrainsMono; }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMochaTheme), nameof(IsGitHubDarkTheme), nameof(IsDarculaTheme), nameof(IsGruvboxTheme))]
    private string _theme = AppThemes.Mocha;

    public bool IsMochaTheme
    {
        get => Theme != AppThemes.GitHubDark && Theme != AppThemes.Darcula && Theme != AppThemes.Gruvbox;
        set { if (value) Theme = AppThemes.Mocha; }
    }

    public bool IsGitHubDarkTheme
    {
        get => Theme == AppThemes.GitHubDark;
        set { if (value) Theme = AppThemes.GitHubDark; }
    }

    public bool IsDarculaTheme
    {
        get => Theme == AppThemes.Darcula;
        set { if (value) Theme = AppThemes.Darcula; }
    }

    public bool IsGruvboxTheme
    {
        get => Theme == AppThemes.Gruvbox;
        set { if (value) Theme = AppThemes.Gruvbox; }
    }

    public event Action<bool>? RequestClose;

    public SettingsViewModel(AppSettings current, SettingsService settingsService, TelegramService telegramService)
    {
        _settingsService = settingsService;
        _telegramService = telegramService;

        PomodoroMinutes = current.PomodoroMinutes;
        Pomodoro2Minutes = current.Pomodoro2Minutes;
        ShortBreakMinutes = current.ShortBreakMinutes;
        InactivityMinutes = current.InactivityMinutes;
        TelegramBotToken = current.TelegramBotToken;
        TelegramChatId = current.TelegramChatId;
        ProxyUrl = current.ProxyUrl;
        TelegramEnabled = current.TelegramEnabled;
        SoundEnabled = current.SoundEnabled;
        Font = current.Font;
        Theme = current.Theme;
    }

    [RelayCommand]
    private async Task TestTelegram()
    {
        TestStatus = "Отправка...";

        var (success, error) = await _telegramService.SendTestAsync(TelegramBotToken, TelegramChatId, ProxyUrl);

        TestStatus = success ? "✅ Сообщение отправлено" : $"❌ Ошибка: {error}";
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
            ProxyUrl = ProxyUrl,
            TelegramEnabled = TelegramEnabled,
            SoundEnabled = SoundEnabled,
            Font = Font,
            Theme = Theme
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
