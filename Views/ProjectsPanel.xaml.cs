using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WorkCheck.Helpers;
using WorkCheck.Models;
using WorkCheck.Services;
using Button = System.Windows.Controls.Button;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using UserControl = System.Windows.Controls.UserControl;

namespace WorkCheck.Views;

// Adding and deleting projects; changes go to the database immediately
public partial class ProjectsPanel : UserControl
{
    private DataService? _dataService;
    private int? _currentProjectId;

    public ProjectsPanel()
    {
        InitializeComponent();
    }

    public void Init(DataService dataService, int? currentProjectId)
    {
        _dataService = dataService;
        _currentProjectId = currentProjectId;
        Reload();
    }

    private void Reload()
    {
        if (_dataService != null)
            ProjectsList.ItemsSource = _dataService.GetAllProjects();
    }

    private void OnNewProjectKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        AddProject();
    }

    private void OnAddClick(object sender, RoutedEventArgs e) => AddProject();

    private void AddProject()
    {
        var name = NewProjectName.Text.Trim();
        if (_dataService == null || string.IsNullOrEmpty(name)) return;

        _dataService.AddProject(new Project { Name = name });
        NewProjectName.Text = "";
        Reload();
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (_dataService == null || sender is not Button { DataContext: Project project }) return;
        var owner = Window.GetWindow(this)!;

        // The tracker keeps saving the running session to the current project, so it must stay
        if (project.Id == _currentProjectId)
        {
            ConfirmWindow.Inform(owner, "Нельзя удалить текущий проект",
                $"Сейчас время записывается на «{project.Name}». Сначала выберите другой проект, потом удаляйте этот.");
            return;
        }

        var (count, total) = _dataService.GetProjectSessionStats(project.Id);
        var message = count == 0
            ? "У проекта нет сессий."
            : $"У проекта {count} {SessionsWord(count)} на {TimeFormatter.FormatShort(total)}. " +
              "Они останутся в статистике под именем «(удалён)», и вернуть их к проекту из приложения будет нельзя.";

        if (!ConfirmWindow.Ask(owner, $"Удалить «{project.Name}»?", message, "Удалить")) return;

        _dataService.DeleteProject(project.Id);
        Reload();
    }

    private static string SessionsWord(int n)
    {
        var mod100 = n % 100;
        if (mod100 is >= 11 and <= 19) return "сессий";
        return (n % 10) switch
        {
            1 => "сессия",
            >= 2 and <= 4 => "сессии",
            _ => "сессий"
        };
    }
}
