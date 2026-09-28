using System.Reflection;
using System.Windows;
using Clipboard = System.Windows.Clipboard;

namespace WorkCheck.Views;

public partial class AboutWindow : Window
{
    private readonly string _appPath;
    private readonly string _dbPath;

    public AboutWindow(string dbPath)
    {
        InitializeComponent();

        _appPath = Environment.ProcessPath ?? Assembly.GetExecutingAssembly().Location;
        _dbPath = dbPath;

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionBlock.Text = version != null ? $"Версия {version.ToString(3)}" : "";

        AppPathBox.Text = _appPath;
        DbPathBox.Text = _dbPath;
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText($"Программа: {_appPath}\nБаза данных: {_dbPath}");
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
