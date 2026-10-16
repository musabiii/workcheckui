using System.Windows;
using System.Windows.Input;
using WorkCheck.Services;
using WorkCheck.ViewModels;

namespace WorkCheck.Views;

public enum SettingsTab { Appearance, Timers, Notifications, Projects }

public partial class SettingsWindow : Window
{
    public SettingsWindow(DataService dataService, int? currentProjectId, SettingsTab tab = SettingsTab.Appearance)
    {
        InitializeComponent();
        Projects.Init(dataService, currentProjectId);

        var tabButton = tab switch
        {
            SettingsTab.Timers => TimersTab,
            SettingsTab.Notifications => NotificationsTab,
            SettingsTab.Projects => ProjectsTab,
            _ => AppearanceTab
        };
        tabButton.IsChecked = true;

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

    private void OnTestTelegramClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            vm.TelegramBotToken = TokenBox.Password;
            vm.TestTelegramCommand.Execute(null);
        }
    }
}
