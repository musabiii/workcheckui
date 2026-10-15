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
