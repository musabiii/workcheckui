using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using WorkCheck.ViewModels;

namespace WorkCheck.Views;

public partial class StatusWindow : Window
{
    private bool _forceClose;
    public Action? OnMinimize;

    public StatusWindow()
    {
        InitializeComponent();
        PositionBottomRight();
    }

    private void PositionBottomRight()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 12;
        Top = workArea.Bottom - Height - 12;
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
        if (sender is not System.Windows.Controls.Button { ContextMenu: { } menu } button) return;
        menu.PlacementTarget = button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
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
