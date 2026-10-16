using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
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
    private readonly NtfyService _ntfy;
    private readonly TrayIconService _trayIcon;
    private readonly SettingsService _settingsService;
    private readonly DataService _dataService;
    private readonly SoundService _soundService;
    private readonly AmbientSoundService _ambient;
    private AppSettings _settings;
    private readonly DispatcherTimer _timer;
    private bool _wasUserShortBreak;
    private Project? _selectedProject;
    private Dictionary<int, string> _projectNames = [];

    private static readonly TimeSpan ProjectRowThreshold = TimeSpan.FromMinutes(15);

    [ObservableProperty] private bool _isWorkMode;
    [ObservableProperty] private string _currentSessionText = "0 мин";
    [ObservableProperty] private string _projectName = "";
    [ObservableProperty] private string _awayTimeText = "0 мин";
    [ObservableProperty] private string _todayWorkedText = "0 мин";
    [ObservableProperty] private IReadOnlyList<ProjectTimeRow> _projectRows = [];
    [ObservableProperty] private string _statusText = "Дрейфую";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAmbientPlaying))]
    private AmbientSound? _playingSound;

    public bool IsAmbientPlaying => PlayingSound != null;

    // The sound the on/off button plays; highlighted in the picker even while off
    [ObservableProperty] private AmbientSound _selectedSound;

    public IReadOnlyList<AmbientGroup> AmbientGroups { get; } =
    [
        new("Шумы", [new(AmbientSound.White, "Белый шум"), new(AmbientSound.Pink, "Розовый шум"), new(AmbientSound.Brown, "Коричневый шум")]),
        new("Работа", [new(AmbientSound.ServerRoom, "Серверная"), new(AmbientSound.Office, "Офис"), new(AmbientSound.Computer, "Компьютер")]),
        new("Атмосфера", [new(AmbientSound.Cyberpunk, "Киберпанк"), new(AmbientSound.RainThunder, "Дождь и гром"), new(AmbientSound.DistantThunder, "Дальний гром")])
    ];

    partial void OnSelectedSoundChanged(AmbientSound value)
    {
        foreach (var option in AmbientGroups.SelectMany(g => g.Options))
            option.IsSelected = option.Sound == value;
    }

    // 0–100 for the slider
    [ObservableProperty] private double _ambientVolume;
    [ObservableProperty] private Brush _statusBrush = DriftingGrayBrush;
    [ObservableProperty] private Brush _sessionBrush = NormalTextBrush;
    [ObservableProperty] private Brush _windowBgBrush = DriftingBgBrush;
    [ObservableProperty] private string _modeButtonText = "▶  В работе";
    [ObservableProperty] private string _awayLabel = "☕  Вне компьютера:";
    [ObservableProperty] private string _sessionIcon = "🖥";
    [ObservableProperty] private string _sessionLabel = "  Текущая сессия:";
    [ObservableProperty] private Brush _compactSessionBrush = DriftingGrayBrush;
    private static Brush ActiveBrush => AppThemes.Brush("Green");
    private static Brush ShortBreakBrush => AppThemes.Brush("Yellow");
    private static Brush InactiveBrush => AppThemes.Brush("Red");
    private static Brush NormalTextBrush => AppThemes.Brush("Subtext1");
    private static Brush DriftingGrayBrush => AppThemes.Brush("Overlay0");
    private static Brush DriftingBgBrush => AppThemes.Brush("DriftBase");
    private static Brush WorkingBgBrush => AppThemes.Brush("Base");

    public StatusViewModel(
        ActivityTracker tracker,
        NotificationService notifications,
        TelegramService telegram,
        NtfyService ntfy,
        SettingsService settingsService,
        DataService dataService,
        AppSettings settings,
        TrayIconService trayIcon,
        SoundService soundService,
        AmbientSoundService ambient)
    {
        _tracker = tracker;
        _notifications = notifications;
        _telegram = telegram;
        _ntfy = ntfy;
        _trayIcon = trayIcon;
        _settingsService = settingsService;
        _dataService = dataService;
        _soundService = soundService;
        _ambient = ambient;
        _settings = settings;

        _ambient.Volume = (float)settings.AmbientVolume;
        _ambientVolume = settings.AmbientVolume * 100;
        SelectedSound = Enum.TryParse<AmbientSound>(settings.AmbientSound, out var sound) ? sound : AmbientSound.Brown;
        OnSelectedSoundChanged(SelectedSound);

        LoadProjects();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTick;
        _timer.Start();

        UpdateTodayTotals();
    }

    private void LoadProjects()
    {
        var projects = _dataService.GetAllProjects();
        _projectNames = projects.ToDictionary(p => p.Id, p => p.Name);
        var active = projects.Where(p => !p.IsArchived).ToList();

        if (active.Count == 0)
        {
            _selectedProject = new Project { Name = "По умолчанию" };
            _selectedProject.Id = _dataService.AddProject(_selectedProject);
            _projectNames[_selectedProject.Id] = _selectedProject.Name;
        }
        else
        {
            var lastSession = _dataService.GetLastSession();
            _selectedProject = active.FirstOrDefault(p => p.Id == lastSession?.ProjectId)
                               ?? active.LastOrDefault();
        }

        if (_selectedProject != null)
        {
            _tracker.CurrentProjectId = _selectedProject.Id;
            ProjectName = _selectedProject.Name;
        }
    }

    private void SelectProject(Project project)
    {
        _selectedProject = project;
        _projectNames[project.Id] = project.Name;
        _tracker.CurrentProjectId = project.Id;
        ProjectName = project.Name;

        UpdateTodayTotals(_tracker.CurrentSession);
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
            // Sent before any overlay: the overlays are modal, and "come back" or "take a rest" is useless
            // once the user is already back; the mode is checked before the overlay lets the user change it
            if (req.SendTelegram && !string.IsNullOrEmpty(req.TelegramText))
                SendRemote(req.TelegramText, req.SilentTelegram);

            if (req.Type is NotificationType.Pomodoro or NotificationType.Pomodoro2)
            {
                var overlayShownAt = DateTime.Now;

                var (choseBreak, description, modeSelected) = _notifications.ShowBreakOverlay(
                    req.Type, req.Title, req.Message,
                    _settings.ShortBreakTime,
                    onBreakStarted: () => _tracker.IsPaused = true,
                    soundService: _soundService,
                    playSoundOnBreakEnd: true,
                    awayBase: GetAwayBase(),
                    todayBase: GetTodayBase(),
                    onBreakEnded: OnBreakCountdownEnded,
                    onStillAway: OnStillAwayAfterBreak,
                    stillAwayAfter: _settings.InactivityTime);
                _tracker.IsPaused = false;

                // Если пользователь нажал "Продолжить работать" сразу (не пошел на перерыв)
                // тогда сессия продолжается до Pomodoro2
                bool continueSession = !choseBreak;
                
                // Корректируем время: пока висел оверлей (ожидание + возможный перерыв),
                // пользователь не работал — это не должно считаться рабочим временем
                _tracker.AccountOverlayIdle(overlayShownAt, description, continueSession);
                ApplyModeSelection(modeSelected);
            }
            else if (req.Type is NotificationType.ShortBreak && !_wasUserShortBreak)
            {
                _wasUserShortBreak = true;
                var overlayShownAt = DateTime.Now;

                var (_, _, modeSelected) = _notifications.ShowBreakOverlay(
                    req.Type, req.Title, req.Message,
                    _settings.ShortBreakTime,
                    onBreakStarted: () => _tracker.IsPaused = true,
                    skipPrompt: true,
                    showChoice: true,
                    soundService: _soundService,
                    fromWorkMode: true,
                    awayBase: GetAwayBase(),
                    todayBase: GetTodayBase());
                _tracker.IsPaused = false;

                _tracker.AccountOverlayIdle(overlayShownAt, "");
                ApplyModeSelection(modeSelected);
            }
            else if (IsWorkMode)
            {
                _notifications.Show(req.Type, req.Title, req.Message, req.SecondaryMessage, req.Quote);
            }
        }
    }

    // The tracker is paused during a break countdown, so these come from the overlay instead of Tick
    private void OnBreakCountdownEnded() => SendRemote("☕ Перерыв окончен, пора возвращаться!");

    private void OnStillAwayAfterBreak()
    {
        if (DateTime.Now.Hour is not (>= 9 and < 18)) return;
        var after = TimeFormatter.FormatHuman(_settings.InactivityTime);
        SendRemote($"❗ Слышь, работать! Перерыв кончился {after} назад\n{Quotes.GetRandom()}");
    }

    // Telegram and ntfy each check their own "enabled" setting
    private void SendRemote(string text, bool silent = false)
    {
        if (!IsWorkMode) return;
        _ = _telegram.SendAsync(text, silent);
        _ = _ntfy.SendAsync(text, silent);
    }

    private void ApplyModeSelection(bool? driftMode)
    {
        if (driftMode is bool drift && drift == IsWorkMode)
            ToggleMode();
    }

    private TimeSpan? GetAwayBase() =>
        IsWorkMode ? _tracker.GetAwayTimeFromDatabase() + _tracker.DisplayAwayTime : null;

    private TimeSpan? GetTodayBase() =>
        IsWorkMode
            ? _dataService.GetTotalWorkTimeByDate(DateTime.Today, IsWorkMode, _tracker.CurrentProjectId) + _tracker.CurrentSession
            : null;

    private void UpdateDisplay()
    {
        var session = _tracker.CurrentSession;
        CurrentSessionText = TimeFormatter.FormatShort(session);
        
        AwayTimeText = TimeFormatter.FormatShort(_tracker.GetAwayTimeFromDatabase() + _tracker.DisplayAwayTime);

        var sessions = _tracker.CompletedSessions;
        SessionLabel = sessions > 0
            ? $"  Текущая сессия ({sessions}):"
            : "  Текущая сессия:";

        UpdateTodayTotals(session);

        WindowBgBrush = IsWorkMode ? WorkingBgBrush : DriftingBgBrush;

        if (IsWorkMode)
        {
            if (session >= _settings.Pomodoro2Time)
                SessionBrush = InactiveBrush;
            else if (session >= _settings.PomodoroTime)
                SessionBrush = ShortBreakBrush;
            else
                SessionBrush = ActiveBrush;

            CompactSessionBrush = SessionBrush;

            _trayIcon.Update((int)session.TotalMinutes, session,
                _settings.PomodoroTime, _settings.Pomodoro2Time, isDrifting: false);

            if (_tracker.UserActive && !_tracker.UserShortBreak)
            {
                _wasUserShortBreak = false;
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
                _wasUserShortBreak = false;
                StatusText = "Неактивен";
                StatusBrush = InactiveBrush;
            }
        }
        else
        {
            _wasUserShortBreak = false;
            SessionBrush = NormalTextBrush;
            CompactSessionBrush = DriftingGrayBrush;
            StatusText = "Дрейфую";
            StatusBrush = DriftingGrayBrush;

            _trayIcon.Update((int)session.TotalMinutes, session,
                _settings.PomodoroTime, _settings.Pomodoro2Time, isDrifting: true);
        }
    }

    private void UpdateTodayTotals(TimeSpan currentSession = default)
    {
        if (!IsWorkMode)
        {
            var todayTotal = _dataService.GetTotalWorkTimeByDate(DateTime.Today, IsWorkMode, _tracker.CurrentProjectId);
            TodayWorkedText = TimeFormatter.FormatShort(todayTotal + currentSession);
            return;
        }

        var totals = _dataService.GetSessionsByDate(DateTime.Today)
            .Where(s => s.IsWorkMode && s.ProjectId.HasValue)
            .GroupBy(s => s.ProjectId!.Value)
            .ToDictionary(g => g.Key, g => TimeSpan.FromMinutes(g.Sum(s => s.Duration.TotalMinutes)));

        var currentId = _selectedProject?.Id;
        if (currentId is int id)
            totals[id] = totals.GetValueOrDefault(id) + currentSession;

        var rows = totals
            .Where(t => _projectNames.ContainsKey(t.Key) && (t.Key == currentId || t.Value >= ProjectRowThreshold))
            .OrderByDescending(t => t.Key == currentId)
            .ThenByDescending(t => t.Value)
            .Select(t => new ProjectTimeRow(_projectNames[t.Key], TimeFormatter.FormatShort(t.Value), t.Key == currentId))
            .ToList();

        if (!rows.SequenceEqual(ProjectRows))
            ProjectRows = rows;
    }

    [RelayCommand]
    private void ManualBreak()
    {
        _tracker.IsPaused = true;
        var overlayShownAt = DateTime.Now;

        var (_, description, modeSelected) = _notifications.ShowBreakOverlay(
            NotificationType.Pomodoro,
            "☕  Ручной перерыв",
            $"Поработали {TimeFormatter.FormatShort(_tracker.CurrentSession)}",
            _settings.ShortBreakTime,
            skipPrompt: true,
            soundService: _soundService,
            fromWorkMode: IsWorkMode,
            awayBase: GetAwayBase(),
            todayBase: GetTodayBase(),
            onBreakEnded: OnBreakCountdownEnded,
            onStillAway: OnStillAwayAfterBreak,
            stillAwayAfter: _settings.InactivityTime);

        _tracker.IsPaused = false;

        _tracker.AccountOverlayIdle(overlayShownAt, description);
        ApplyModeSelection(modeSelected);
    }

    [RelayCommand]
    private void Reset()
    {
        _tracker.Reset();
        CurrentSessionText = "0 мин";
        AwayTimeText = "0 мин";
        UpdateTodayTotals();
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

        CurrentSessionText = "0 мин";
        AwayTimeText = "0 мин";

        if (IsWorkMode)
        {
            ModeButtonText = "⏸  Дрейфую";
            WindowBgBrush = WorkingBgBrush;
            AwayLabel = "☕  Вне компьютера:";
            SessionIcon = "🟢";
        }
        else
        {
            ModeButtonText = "▶  В работе";
            WindowBgBrush = DriftingBgBrush;
            AwayLabel = "";
            SessionIcon = "🖥";
        }
    }

    [RelayCommand]
    private void OpenStatistics()
    {
        var window = new StatisticsWindow(_dataService) { Owner = Application.Current.MainWindow };
        window.ShowDialog();
    }

    public IReadOnlyList<Project> GetProjects() => _dataService.GetAllProjects().Where(p => !p.IsArchived).ToList();

    public int? CurrentProjectId => _selectedProject?.Id;

    [RelayCommand]
    private void ChooseProject(Project project) => SelectProject(project);

    [RelayCommand]
    private void OpenProjectSettings() => ShowSettings(SettingsTab.Projects);

    [RelayCommand]
    private void OpenSettings() => ShowSettings(SettingsTab.Appearance);

    private void ShowSettings(SettingsTab tab)
    {
        var vm = new SettingsViewModel(_settings, _settingsService, _telegram, _ntfy);
        var window = new SettingsWindow(_dataService, _selectedProject?.Id, tab) { DataContext = vm };
        vm.RequestClose += result => { window.DialogResult = result; };

        var saved = window.ShowDialog() == true;

        // Project edits apply immediately, even when the rest of the settings is cancelled
        _projectNames = _dataService.GetAllProjects().ToDictionary(p => p.Id, p => p.Name);
        UpdateTodayTotals(_tracker.CurrentSession);

        if (saved)
        {
            _settings = _settingsService.Load();
            _tracker.ApplySettings(_settings);
            _telegram.Settings = _settings;
            _ntfy.Settings = _settings;
            _soundService.Enabled = _settings.SoundEnabled;
            AppThemes.Apply(_settings.Theme);
            AppFonts.Apply(_settings.Font, _settings.Theme);
        }
    }

    [RelayCommand]
    private void CloseApp()
    {
        Application.Current.MainWindow?.Hide();
    }

    [RelayCommand]
    private void OpenAbout()
    {
        var window = new AboutWindow(_dataService.DbPath);
        window.ShowDialog();
    }

    [RelayCommand]
    private void ToggleAmbient()
    {
        if (PlayingSound != null)
            _ambient.Stop();
        else
            _ambient.Play(SelectedSound);
        PlayingSound = _ambient.Current;
    }

    // Picking a sound also turns it on: that is almost always why the picker was opened
    [RelayCommand]
    private void SelectAmbient(AmbientSound sound)
    {
        SelectedSound = sound;
        _ambient.Play(sound);
        PlayingSound = _ambient.Current;

        _settings.AmbientSound = sound.ToString();
        _settingsService.Save(_settings);
    }

    partial void OnAmbientVolumeChanged(double value)
    {
        _ambient.Volume = (float)(value / 100);
        _settings.AmbientVolume = value / 100;
    }

    // The slider moves in small steps, so the volume is written to disk once the popup closes
    public void SaveAmbientVolume() => _settingsService.Save(_settings);

    public void Cleanup()
    {
        _ambient.Stop();
        _timer.Stop();
        _tracker.StopHooks();
        _notifications.CloseAll();
        _trayIcon.Dispose();
        Debug.WriteLine($"[WorkCheck] {_tracker.GetSummary()}");
    }
}
