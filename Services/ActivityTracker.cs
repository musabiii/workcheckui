using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Application = System.Windows.Application;
using WorkCheck.Helpers;
using WorkCheck.Models;

namespace WorkCheck.Services;

public sealed class ActivityTracker : IDisposable
{
    #region Win32 Hooks

    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;

    private delegate IntPtr LowLevelHookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelHookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private IntPtr _keyboardHookId;
    private IntPtr _mouseHookId;
    private readonly LowLevelHookProc _keyboardProc;
    private readonly LowLevelHookProc _mouseProc;

    #endregion

    private TimeSpan _pomodoroTime;
    private TimeSpan _pomodoro2Time;
    private TimeSpan _shortBreakTime;
    private TimeSpan _inactivityTime;

    private bool _userActive = true;
    private bool _userShortBreak;
    private bool _pomodoroNotified;
    private bool _pomodoro2Notified;

    private DateTime _lastActivityTime;
    private DateTime _lastInactivityTime;
    private TimeSpan _workedTime = TimeSpan.Zero;
    private TimeSpan _awayTime = TimeSpan.Zero;
    private DateTime _activeSessionStart;

    private DateTime _lastHookFire = DateTime.MinValue;
    private static readonly TimeSpan HookThrottle = TimeSpan.FromMilliseconds(100);

    private readonly Queue<NotificationRequest> _pending = new();
    private bool _disposed;

    public bool UserActive => _userActive;
    public bool UserShortBreak => _userShortBreak;

    public TimeSpan CurrentSession
    {
        get
        {
            if (_userActive && !_userShortBreak)
                return DateTime.Now - _activeSessionStart;
            return TimeSpan.Zero;
        }
    }

    public TimeSpan DisplayWorkedTime
    {
        get
        {
            var total = _workedTime;
            if (_userActive && !_userShortBreak)
                total += _lastActivityTime - _lastInactivityTime;
            return total;
        }
    }

    public TimeSpan DisplayAwayTime
    {
        get
        {
            var total = _awayTime;
            if (!_userActive || _userShortBreak)
                total += DateTime.Now - _lastActivityTime;
            return total;
        }
    }

    public ActivityTracker(AppSettings settings)
    {
        _keyboardProc = KeyboardHookCallback;
        _mouseProc = MouseHookCallback;

        ApplySettings(settings);

        var now = DateTime.Now;
        _lastActivityTime = now;
        _lastInactivityTime = now;
        _activeSessionStart = now;
    }

    public void ApplySettings(AppSettings settings)
    {
        _pomodoroTime = settings.PomodoroTime;
        _pomodoro2Time = settings.Pomodoro2Time;
        _shortBreakTime = settings.ShortBreakTime;
        _inactivityTime = settings.InactivityTime;
    }

    public void OverrideTimings(TimeSpan? pomodoro = null, TimeSpan? pomodoro2 = null,
        TimeSpan? shortBreak = null, TimeSpan? inactivity = null)
    {
        if (pomodoro.HasValue) _pomodoroTime = pomodoro.Value;
        if (pomodoro2.HasValue) _pomodoro2Time = pomodoro2.Value;
        if (shortBreak.HasValue) _shortBreakTime = shortBreak.Value;
        if (inactivity.HasValue) _inactivityTime = inactivity.Value;
    }

    public void StartHooks()
    {
        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule!;
        var hMod = GetModuleHandle(module.ModuleName);

        _keyboardHookId = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, hMod, 0);
        _mouseHookId = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, hMod, 0);

        if (_keyboardHookId == IntPtr.Zero)
            Debug.WriteLine("[Hooks] Не удалось установить клавиатурный хук");
        if (_mouseHookId == IntPtr.Zero)
            Debug.WriteLine("[Hooks] Не удалось установить хук мыши");
    }

    public void StopHooks()
    {
        _disposed = true;

        if (_keyboardHookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHookId);
            _keyboardHookId = IntPtr.Zero;
        }
        if (_mouseHookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHookId);
            _mouseHookId = IntPtr.Zero;
        }
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try { if (nCode >= 0) OnHookFired(); }
        catch (Exception ex) { Debug.WriteLine($"[Hooks] Keyboard: {ex.Message}"); }
        return CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try { if (nCode >= 0) OnHookFired(); }
        catch (Exception ex) { Debug.WriteLine($"[Hooks] Mouse: {ex.Message}"); }
        return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
    }

    private void OnHookFired()
    {
        var now = DateTime.Now;
        if (now - _lastHookFire < HookThrottle) return;
        _lastHookFire = now;

        if (_disposed) return;

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted) return;

        dispatcher.BeginInvoke(OnActivity, DispatcherPriority.Input);
    }

    private void OnActivity()
    {
        var now = DateTime.Now;

        if (!_userActive || _userShortBreak)
        {
            var awayInterval = now - _lastActivityTime;
            _awayTime += awayInterval;

            _pending.Enqueue(new NotificationRequest
            {
                Type = NotificationType.Welcome,
                Title = "С возвращением!",
                Message = $"Вас не было {TimeFormatter.FormatHuman(awayInterval)}!",
                SecondaryMessage = $"Всего вне работы: {TimeFormatter.FormatHuman(_awayTime)}",
                Quote = Quotes.GetRandom()
            });

            _pomodoroNotified = false;
            _pomodoro2Notified = false;
            _lastInactivityTime = now;
            _activeSessionStart = now;
        }

        _lastActivityTime = now;
        _userActive = true;
        _userShortBreak = false;
    }

    public List<NotificationRequest> Tick()
    {
        var notifications = new List<NotificationRequest>();

        while (_pending.Count > 0)
            notifications.Add(_pending.Dequeue());

        if (!_userActive)
            return notifications;

        var now = DateTime.Now;
        var sinceLast = now - _lastActivityTime;
        var sinceInactivity = now - _lastInactivityTime;

        if (!_userShortBreak)
        {
            if (sinceInactivity > _pomodoroTime && !_pomodoroNotified)
            {
                notifications.Add(new NotificationRequest
                {
                    Type = NotificationType.Pomodoro,
                    Title = "Пора отдохнуть!",
                    Message = $"Поработали {TimeFormatter.FormatHuman(sinceInactivity)}"
                });
                _pomodoroNotified = true;
            }

            if (sinceInactivity > _pomodoro2Time && !_pomodoro2Notified)
            {
                notifications.Add(new NotificationRequest
                {
                    Type = NotificationType.Pomodoro2,
                    Title = "Пора отдохнуть!",
                    Message = $"Поработали {TimeFormatter.FormatHuman(sinceInactivity)}",
                    SendTelegram = true,
                    TelegramText = "Отдохни!"
                });
                _pomodoro2Notified = true;
            }
        }

        if (!_userShortBreak && sinceLast > _shortBreakTime)
        {
            _userShortBreak = true;
            _pomodoroNotified = true;

            var sessionWork = _lastActivityTime - _lastInactivityTime;
            if (sessionWork > TimeSpan.Zero)
                _workedTime += sessionWork;

            notifications.Add(new NotificationRequest
            {
                Type = NotificationType.ShortBreak,
                Title = "Короткий отдых",
                Message = $"Поработали {TimeFormatter.FormatHuman(sessionWork)}.",
                SecondaryMessage = $"Всего отработано: {TimeFormatter.FormatHuman(_workedTime)}",
                SendTelegram = true,
                TelegramText = "Конец короткого отдыха!",
                SilentTelegram = true
            });
        }

        if (sinceLast > _inactivityTime)
        {
            _userActive = false;

            bool isWorkHours = now.Hour is >= 9 and < 18;
            if (isWorkHours)
            {
                var quote = Quotes.GetRandom();
                notifications.Add(new NotificationRequest
                {
                    Type = NotificationType.Inactivity,
                    Title = "❗ Работать!",
                    Message = $"Прошло {TimeFormatter.FormatHuman(_inactivityTime)}",
                    Quote = quote,
                    SendTelegram = true,
                    TelegramText = $"❗ Слышь, работать! Прошло {TimeFormatter.FormatHuman(_inactivityTime)}\n{quote}"
                });
            }
            else
            {
                Debug.WriteLine("[ActivityTracker] Неактивность в нерабочее время — пропуск уведомления");
            }
        }

        return notifications;
    }

    public void Reset()
    {
        var now = DateTime.Now;
        _lastActivityTime = now;
        _lastInactivityTime = now;
        _activeSessionStart = now;
        _workedTime = TimeSpan.Zero;
        _awayTime = TimeSpan.Zero;
        _userActive = true;
        _userShortBreak = false;
        _pomodoroNotified = false;
        _pomodoro2Notified = false;
        _pending.Clear();
    }

    public string GetSummary() =>
        $"Завершение. Всего отработано: {TimeFormatter.FormatHuman(DisplayWorkedTime)}; " +
        $"вне работы: {TimeFormatter.FormatHuman(DisplayAwayTime)}";

    public void Dispose() => StopHooks();
}
