using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Application = System.Windows.Application;
using WorkCheck.Helpers;
using WorkCheck.Models;
using WorkCheck.Services;
using WorkCheck.ViewModels;
using WorkCheck.Views;

namespace WorkCheck;

public partial class App : Application
{
    private ActivityTracker? _tracker;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        var settingsService = new SettingsService();
        var settings = settingsService.Load();

        var telegramService = new TelegramService(settings);
        var notificationService = new NotificationService();
        _tracker = new ActivityTracker(settings);

        ApplyCommandLineArgs(e.Args, _tracker);

        StatusWindow? statusWindow = null;

        var trayIcon = new TrayIconService(
            onExit: () =>
            {
                statusWindow?.Shutdown();
                Shutdown();
            },
            onToggleWindow: () =>
            {
                if (statusWindow == null) return;
                if (statusWindow.IsVisible)
                    statusWindow.Hide();
                else
                {
                    statusWindow.Show();
                    statusWindow.Activate();
                }
            });

        var statusVm = new StatusViewModel(_tracker, notificationService, telegramService, settingsService, settings, trayIcon);
        statusWindow = new StatusWindow { DataContext = statusVm };

        MainWindow = statusWindow;
        statusWindow.Show();

        _tracker.StartHooks();

        Debug.WriteLine($"[WorkCheck] Запуск. {Quotes.GetRandom()}");
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Debug.WriteLine($"[WorkCheck] Необработанное исключение: {e.Exception}");
        e.Handled = true;
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Debug.WriteLine($"[WorkCheck] Необработанная задача: {e.Exception}");
        e.SetObserved();
    }

    private static void ApplyCommandLineArgs(string[] args, ActivityTracker tracker)
    {
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLower())
            {
                case "-d" or "--debug":
                    tracker.OverrideTimings(
                        pomodoro: TimeSpan.FromSeconds(10),
                        pomodoro2: TimeSpan.FromSeconds(40),
                        shortBreak: TimeSpan.FromSeconds(5),
                        inactivity: TimeSpan.FromSeconds(15));
                    Debug.WriteLine("[CLI] Режим отладки: Pomodoro=10с, ShortBreak=5с, Inactivity=15с");
                    break;

                case "-l" or "--long":
                    tracker.OverrideTimings(
                        pomodoro: TimeSpan.FromMinutes(40),
                        pomodoro2: TimeSpan.FromMinutes(60),
                        shortBreak: TimeSpan.FromMinutes(10),
                        inactivity: TimeSpan.FromMinutes(25));
                    Debug.WriteLine("[CLI] Длинный режим: Pomodoro=40м, ShortBreak=10м, Inactivity=25м");
                    break;

                case "-p" or "--pomodoro":
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out var minutes))
                    {
                        tracker.OverrideTimings(pomodoro: TimeSpan.FromMinutes(minutes));
                        i++;
                        Debug.WriteLine($"[CLI] Pomodoro={minutes}м");
                    }
                    break;
            }
        }
    }
}
