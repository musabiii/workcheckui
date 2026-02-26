using System.Windows;
using System.Windows.Input;
using WorkCheck.ViewModels;

namespace WorkCheck.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
            TokenBox.Password = vm.TelegramBotToken;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        DragMove();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            vm.TelegramBotToken = TokenBox.Password;
            vm.SaveCommand.Execute(null);
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
            vm.CancelCommand.Execute(null);
    }
}
