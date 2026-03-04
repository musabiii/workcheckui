using System.Windows;
using WorkCheck.Models;
using WorkCheck.Services;

namespace WorkCheck.Views;

public partial class StatisticsWindow : Window
{
    private readonly DataService _dataService;

    public StatisticsWindow(DataService dataService)
    {
        InitializeComponent();
        _dataService = dataService;
        LoadStatistics();
    }

    private void LoadStatistics()
    {
        var sessions = _dataService.GetSessionsByDate(DateTime.Today);
        var totalTime = TimeSpan.FromTicks(sessions.Sum(s => s.Duration.Ticks));

        SessionCountText.Text = sessions.Count.ToString();
        TotalTimeText.Text = FormatTime(totalTime);

        SessionsList.ItemsSource = sessions.Select(s => new
        {
            s.StartTime,
            s.EndTime,
            DurationText = FormatTime(s.Duration)
        }).ToList();
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
