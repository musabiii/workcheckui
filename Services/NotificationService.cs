using System.Diagnostics;
using System.Windows;
using WorkCheck.Models;
using WorkCheck.Views;

namespace WorkCheck.Services;

public class NotificationService
{
    private readonly List<NotificationWindow> _active = [];
    private bool _repositioning;

    private const double WindowWidth = 400;
    private const double Margin = 2;
    private const double Gap = 4;
    private const double StatusWindowHeight = 180;

    public void Show(NotificationType type, string title, string message,
        string? secondary = null, string? quote = null)
    {
        try
        {
            var window = new NotificationWindow(type, title, message, secondary, quote);
            window.Closed += OnWindowClosed;

            var workArea = SystemParameters.WorkArea;
            window.Left = workArea.Right - WindowWidth - Margin;
            window.Top = workArea.Bottom - StatusWindowHeight - Margin - 120;

            window.Show();

            _active.Add(window);
            RepositionAll();
            window.PlayFadeIn();

            window.Loaded += (_, _) => RepositionAll();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Notification] Ошибка показа: {ex.Message}");
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is NotificationWindow w)
        {
            w.Closed -= OnWindowClosed;
            _active.Remove(w);

            if (!_repositioning)
                RepositionAll();
        }
    }

    private void RepositionAll()
    {
        if (_repositioning) return;
        _repositioning = true;

        try
        {
            var workArea = SystemParameters.WorkArea;
            double x = workArea.Right - WindowWidth - Margin;
            double bottom = workArea.Bottom - StatusWindowHeight - Margin;

            var snapshot = _active.ToList();
            foreach (var w in snapshot)
            {
                try
                {
                    double h = w.ActualHeight > 0 ? w.ActualHeight : 120;
                    bottom -= h + Gap;
                    w.Left = x;
                    w.Top = bottom;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Notification] Ошибка позиционирования: {ex.Message}");
                }
            }
        }
        finally
        {
            _repositioning = false;
        }
    }

    public bool ShowBreakOverlay(NotificationType type, string title, string message,
        TimeSpan breakDuration, Action? onBreakStarted = null)
    {
        try
        {
            var overlay = new BreakOverlayWindow(type, title, message, breakDuration);
            overlay.OnBreakStarted = onBreakStarted;
            overlay.Loaded += (_, _) => overlay.PlayFadeIn();
            overlay.ShowWithOverlays();
            return overlay.UserChoseBreak;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Notification] Ошибка показа оверлея: {ex.Message}");
            return false;
        }
    }

    public void CloseAll()
    {
        var copy = _active.ToList();
        _active.Clear();

        foreach (var w in copy)
        {
            try
            {
                w.Closed -= OnWindowClosed;
                w.ForceClose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Notification] Ошибка закрытия: {ex.Message}");
            }
        }
    }
}
