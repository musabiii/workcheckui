using System.Windows;
using System.Windows.Input;
using WorkCheck.ViewModels;

namespace WorkCheck.Views;

public partial class StatusWindow : Window
{
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

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is StatusViewModel vm)
            vm.Cleanup();
        base.OnClosed(e);
    }
}
