using System.Windows;
using WorkCheck.Models;
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
        TotalTimeText.Text = FormatTime(totalTime);

        SessionsGrid.ItemsSource = sessions.Select(s => new
        {
            s.StartTime,
            s.EndTime,
            DurationText = FormatTime(s.Duration),
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

    private static string FormatTime(TimeSpan time)
    {
        if (time.TotalHours >= 1)
            return $"{time.Hours} ч {time.Minutes} мин";
        if (time.Minutes >= 1)
            return $"{time.Minutes} мин";
        return $"{time.Seconds} сек";
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
