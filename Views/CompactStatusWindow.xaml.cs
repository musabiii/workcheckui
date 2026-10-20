using System.ComponentModel;
using System.Windows;
using WorkCheck.Helpers;

namespace WorkCheck.Views;

public partial class CompactStatusWindow : ThemedWindow
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
        Left = workArea.Right - Width + AppThemes.WindowShadowSpace - 12;
        Top = workArea.Bottom - Height + AppThemes.WindowShadowSpace - 12;
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