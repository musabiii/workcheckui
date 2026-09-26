using System.Windows;
using WorkCheck.Models;
using WorkCheck.Services;

namespace WorkCheck.Views;

public partial class ProjectsWindow : Window
{
    private readonly DataService _dataService;
    private readonly Action<Project> _onSelectProject;

    public ProjectsWindow(DataService dataService, Project? currentProject, Action<Project> onSelectProject)
    {
        InitializeComponent();

        _dataService = dataService;
        _onSelectProject = onSelectProject;

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

        var project = new Project { Name = name };
        var id = _dataService.AddProject(project);

        project.Id = id;
        ((List<Project>)ProjectsList.ItemsSource).Add(project);
        ProjectsList.ItemsSource = null;
        ProjectsList.ItemsSource = _dataService.GetAllProjects();

        NewProjectName.Text = "";
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is int id)
        {
            _dataService.DeleteProject(id);
            ProjectsList.ItemsSource = _dataService.GetAllProjects();
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}