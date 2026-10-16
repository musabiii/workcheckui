using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using WorkCheck.Helpers;
using WorkCheck.ViewModels;
using Button = System.Windows.Controls.Button;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;

namespace WorkCheck.Views;

public partial class StatusWindow : Window
{
    private bool _forceClose;
    public Action? OnMinimize;

    public StatusWindow()
    {
        InitializeComponent();
        Left = SystemParameters.WorkArea.Right - Width - 12;
        SizeChanged += OnSizeChanged;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.HeightChanged) return;

        // Keep the bottom edge in place when project rows appear or disappear
        if (e.PreviousSize.Height == 0)
            Top = SystemParameters.WorkArea.Bottom - e.NewSize.Height - 12;
        else
            Top -= e.NewSize.Height - e.PreviousSize.Height;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        DragMove();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_forceClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        if (DataContext is StatusViewModel vm)
            vm.Cleanup();
        base.OnClosing(e);
    }

    private void OnMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button) return;
        OpenMenu(menu, button);
    }

    // The project list is rebuilt on every open so it reflects projects added or deleted in settings
    private void OnProjectsClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button || DataContext is not StatusViewModel vm) return;

        menu.Items.Clear();
        foreach (var project in vm.GetProjects())
        {
            var isCurrent = project.Id == vm.CurrentProjectId;
            menu.Items.Add(new MenuItem
            {
                Header = new TextBlock
                {
                    Text = project.Name,
                    FontWeight = isCurrent ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = AppThemes.Brush(isCurrent ? "Yellow" : "Text")
                },
                Command = vm.ChooseProjectCommand,
                CommandParameter = project
            });
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "⚙  Управление проектами", Command = vm.OpenProjectSettingsCommand });

        OpenMenu(menu, button);
    }

    private static void OpenMenu(ContextMenu menu, Button button)
    {
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private DateTime _soundPopupClosedAt;

    private void OnSoundPickerClick(object sender, RoutedEventArgs e)
    {
        // A click on the button first closes the open popup; don't reopen it right away
        if ((DateTime.Now - _soundPopupClosedAt).TotalMilliseconds < 300) return;
        SoundPopup.IsOpen = true;
    }

    private void OnSoundChosen(object sender, RoutedEventArgs e) => SoundPopup.IsOpen = false;

    private void OnSoundPopupClosed(object? sender, EventArgs e)
    {
        _soundPopupClosedAt = DateTime.Now;
        if (DataContext is StatusViewModel vm)
            vm.SaveAmbientVolume();
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e)
    {
        OnMinimize?.Invoke();
    }

    public void Shutdown()
    {
        _forceClose = true;
        Close();
    }
}
