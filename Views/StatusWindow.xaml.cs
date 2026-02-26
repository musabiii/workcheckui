using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using WorkCheck.ViewModels;

namespace WorkCheck.Views;

public partial class StatusWindow : Window
{
    private bool _forceClose;

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

    public void Shutdown()
    {
        _forceClose = true;
        Close();
    }
}
