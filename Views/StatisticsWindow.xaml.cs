using System.Windows;
using WorkCheck.Helpers;
using WorkCheck.Services;

namespace WorkCheck.Views;

public partial class StatisticsWindow : Window
{
    private static readonly TimeSpan FullSessionDuration = TimeSpan.FromMinutes(25);

    private readonly DataService _dataService;
    private DateTime _currentDate;

    public StatisticsWindow(DataService dataService)
    {
        InitializeComponent();
        _dataService = dataService;
        _currentDate = DateTime.Today;
        LoadStatistics();
    }

    private void LoadStatistics()
    {
        DateText.Text = _currentDate.ToString("dd.MM.yyyy");
        
        var minDuration = AllSessionsRadio.IsChecked == true ? TimeSpan.FromMinutes(1) : FullSessionDuration;
        var sessions = _dataService.GetSessionsByDate(_currentDate)
            .Where(s => s.IsWorkMode && s.Duration >= minDuration)
            .ToList();
        var totalTime = TimeSpan.FromTicks(sessions.Sum(s => s.Duration.Ticks));

        SessionCountText.Text = sessions.Count.ToString();
        TotalTimeText.Text = TimeFormatter.FormatShort(totalTime);

        var projectNames = _dataService.GetAllProjects().ToDictionary(p => p.Id, p => p.Name);

        SessionsGrid.ItemsSource = sessions.Select(s => new
        {
            s.StartTime,
            s.EndTime,
            DurationText = TimeFormatter.FormatShort(s.Duration),
            ProjectName = s.ProjectId is int id && projectNames.TryGetValue(id, out var name) ? name : "(удалён)",
            s.Description
        }).ToList();
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        // Checked fires while InitializeComponent sets the default; the constructor loads the data itself
        if (!IsLoaded) return;
        LoadStatistics();
    }

    private void OnPreviousDayClick(object sender, RoutedEventArgs e)
    {
        _currentDate = _currentDate.AddDays(-1);
        LoadStatistics();
    }

    private void OnNextDayClick(object sender, RoutedEventArgs e)
    {
        if (_currentDate < DateTime.Today)
        {
            _currentDate = _currentDate.AddDays(1);
            LoadStatistics();
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
