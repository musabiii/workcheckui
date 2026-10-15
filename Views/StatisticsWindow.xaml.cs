using System.Windows;
using WorkCheck.Helpers;
using WorkCheck.Services;

namespace WorkCheck.Views;

public partial class StatisticsWindow : Window
{
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
        
        var sessions = _dataService.GetSessionsByDate(_currentDate)
            .Where(s => s.IsWorkMode && s.Duration >= TimeSpan.FromMinutes(25))
            .ToList();
        var totalTime = TimeSpan.FromTicks(sessions.Sum(s => s.Duration.Ticks));

        SessionCountText.Text = sessions.Count.ToString();
        TotalTimeText.Text = TimeFormatter.FormatShort(totalTime);

        SessionsGrid.ItemsSource = sessions.Select(s => new
        {
            s.StartTime,
            s.EndTime,
            DurationText = TimeFormatter.FormatShort(s.Duration),
            s.Description
        }).ToList();
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
