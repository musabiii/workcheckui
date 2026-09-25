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
            }
        };
    }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        var name = NewProjectName.Text.Trim();
        if (string.IsNullOrEmpty(name)) return;

        var rateText = NewProjectRate.Text.Trim();
        var rate = decimal.TryParse(rateText, out var r) ? r : 0m;

        var project = new Project { Name = name, Rate = rate };
        var id = _dataService.AddProject(project);

        project.Id = id;
        ((List<Project>)ProjectsList.ItemsSource).Add(project);
        ProjectsList.ItemsSource = null;
        ProjectsList.ItemsSource = _dataService.GetAllProjects();

        NewProjectName.Text = "";
        NewProjectRate.Text = "";
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