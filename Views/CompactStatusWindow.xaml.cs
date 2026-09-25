using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace WorkCheck.Views;

public partial class CompactStatusWindow : Window
{
    public Action? OnExpand;

    public CompactStatusWindow()
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
        e.Cancel = true;
        Hide();
    }

    protected void OnExpandClick(object sender, RoutedEventArgs e)
    {
        OnExpand?.Invoke();
    }
}