using System.Windows;
using System.Windows.Input;
using WorkCheck.Helpers;

namespace WorkCheck.Views;

public partial class ConfirmWindow : Window
{
    private ConfirmWindow(string title, string message, string confirmText, string? cancelText)
    {
        InitializeComponent();
        WindowBackdrop.ApplyIfGlass(this);
        TitleBlock.Text = title;
        MessageBlock.Text = message;
        ConfirmButton.Content = confirmText;

        if (cancelText == null)
        {
            CancelButton.Visibility = Visibility.Collapsed;
            ConfirmButton.Style = (Style)FindResource("PrimaryButton");
        }
        else
        {
            CancelButton.Content = cancelText;
        }
    }

    public static bool Ask(Window owner, string title, string message, string confirmText) =>
        new ConfirmWindow(title, message, confirmText, "Отмена") { Owner = owner }.ShowDialog() == true;

    public static void Inform(Window owner, string title, string message) =>
        new ConfirmWindow(title, message, "Понятно", null) { Owner = owner }.ShowDialog();

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        DragMove();
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
