using System.Windows;
using WorkCheck.Helpers;
using WorkCheck.Models;
using WorkCheck.Services;

namespace WorkCheck.Views;

public partial class ProjectsWindow : Window
{
    private readonly DataService _dataService;
    private readonly Action<Project> _onSelectProject;
    private readonly int? _currentProjectId;
    private bool _closing;
    private bool _inDialog;

    public ProjectsWindow(DataService dataService, Project? currentProject, Action<Project> onSelectProject)
    {
        InitializeComponent();

        _dataService = dataService;
        _onSelectProject = onSelectProject;
        _currentProjectId = currentProject?.Id;

        // Clicking anywhere outside closes the list; the delete dialogs keep it open
        Deactivated += (_, _) =>
        {
            if (!_closing && !_inDialog) Close();
        };
        Closing += (_, _) => _closing = true;

        var projects = dataService.GetAllProjects();
        ProjectsList.ItemsSource = projects;

        if (currentProject != null)
        {
            ProjectsList.SelectedItem = projects.FirstOrDefault(p => p.Id == currentProject.Id);
        }

        ProjectsList.SelectionChanged += (s, e) =>
        {
            if (ProjectsList.SelectedItem is Project selected)
            {
                _onSelectProject(selected);
                Close();
            }
        };

        ProjectsList.PreviewMouseLeftButtonUp += (s, e) =>
        {
            if (e.OriginalSource is not DependencyObject source) return;
            if (System.Windows.Controls.ItemsControl.ContainerFromElement(ProjectsList, source) is not System.Windows.Controls.ListBoxItem item) return;
            if (FindParent<System.Windows.Controls.Button>(source) != null) return;
            if (item.DataContext is Project clicked && ReferenceEquals(clicked, ProjectsList.SelectedItem))
            {
                _onSelectProject(clicked);
                Close();
            }
        };
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        for (var d = child; d != null; d = System.Windows.Media.VisualTreeHelper.GetParent(d))
            if (d is T match) return match;
        return null;
    }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        var name = NewProjectName.Text.Trim();
        if (string.IsNullOrEmpty(name)) return;

        _dataService.AddProject(new Project { Name = name });
        ProjectsList.ItemsSource = _dataService.GetAllProjects();

        NewProjectName.Text = "";
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { DataContext: Project project }) return;

        // The tracker keeps saving the running session to the current project, so it must stay
        if (project.Id == _currentProjectId)
        {
            InDialog(() => ConfirmWindow.Inform(this, "Нельзя удалить текущий проект",
                $"Сейчас время записывается на «{project.Name}». Сначала выберите другой проект, потом удаляйте этот."));
            return;
        }

        var (count, total) = _dataService.GetProjectSessionStats(project.Id);
        var message = count == 0
            ? "У проекта нет сессий."
            : $"У проекта {count} {SessionsWord(count)} на {TimeFormatter.FormatShort(total)}. " +
              "Они останутся в статистике под именем «(удалён)», и вернуть их к проекту из приложения будет нельзя.";

        var confirmed = false;
        InDialog(() => confirmed = ConfirmWindow.Ask(this, $"Удалить «{project.Name}»?", message, "Удалить"));
        if (!confirmed) return;

        _dataService.DeleteProject(project.Id);
        ProjectsList.ItemsSource = _dataService.GetAllProjects();
    }

    // Focus may not come back to this window when a dialog closes, so take it back explicitly
    private void InDialog(Action show)
    {
        _inDialog = true;
        try { show(); }
        finally { _inDialog = false; }
        Activate();
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

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}