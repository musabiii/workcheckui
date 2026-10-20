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
    private readonly NtfyService _ntfyService;
    private readonly AppSettings _current;

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
    [ObservableProperty] private string _ntfyServer = "";
    [ObservableProperty] private string _ntfyTopic = "";
    [ObservableProperty] private bool _ntfyEnabled;
    [ObservableProperty] private string _ntfyTestStatus = "";

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
    [NotifyPropertyChangedFor(nameof(IsMochaTheme), nameof(IsGitHubDarkTheme), nameof(IsDarculaTheme), nameof(IsGruvboxTheme),
        nameof(IsTokyoNightTheme), nameof(IsDraculaTheme), nameof(IsNordTheme))]
    private string _theme = AppThemes.Mocha;

    public bool IsMochaTheme
    {
        get => Theme == AppThemes.Mocha || !AppThemes.IsKnown(Theme);
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

    public bool IsTokyoNightTheme
    {
        get => Theme == AppThemes.TokyoNight;
        set { if (value) Theme = AppThemes.TokyoNight; }
    }

    public bool IsDraculaTheme
    {
        get => Theme == AppThemes.Dracula;
        set { if (value) Theme = AppThemes.Dracula; }
    }

    public bool IsNordTheme
    {
        get => Theme == AppThemes.Nord;
        set { if (value) Theme = AppThemes.Nord; }
    }

    public event Action<bool>? RequestClose;

    public SettingsViewModel(AppSettings current, SettingsService settingsService, TelegramService telegramService,
        NtfyService ntfyService)
    {
        _settingsService = settingsService;
        _telegramService = telegramService;
        _ntfyService = ntfyService;
        _current = current;

        PomodoroMinutes = current.PomodoroMinutes;
        Pomodoro2Minutes = current.Pomodoro2Minutes;
        ShortBreakMinutes = current.ShortBreakMinutes;
        InactivityMinutes = current.InactivityMinutes;
        TelegramBotToken = current.TelegramBotToken;
        TelegramChatId = current.TelegramChatId;
        ProxyUrl = current.ProxyUrl;
        TelegramEnabled = current.TelegramEnabled;
        NtfyServer = current.NtfyServer;
        NtfyTopic = current.NtfyTopic;
        NtfyEnabled = current.NtfyEnabled;
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
    private async Task TestNtfy()
    {
        NtfyTestStatus = "Отправка...";

        var (success, error) = await _ntfyService.SendTestAsync(NtfyServer, NtfyTopic);

        NtfyTestStatus = success ? "✅ Сообщение отправлено" : $"❌ Ошибка: {error}";
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
            NtfyServer = string.IsNullOrWhiteSpace(NtfyServer) ? NtfyService.DefaultServer : NtfyServer.Trim(),
            NtfyTopic = NtfyTopic.Trim(),
            NtfyEnabled = NtfyEnabled,
            SoundEnabled = SoundEnabled,
            Font = Font,
            Theme = Theme,
            AmbientSound = _current.AmbientSound,
            AmbientVolume = _current.AmbientVolume
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
