using System.Windows;
using WorkCheck.Services;
using Application = System.Windows.Application;

namespace WorkCheck.Views;

public partial class SessionSummaryWindow : Window
{
    public SessionSummaryWindow(
        DataService dataService,
        TimeSpan workTime,
        TimeSpan awayTime,
        DateTime startTime,
        DateTime endTime,
        bool isWorkMode)
    {
        InitializeComponent();
        
        WorkTimeText.Text = FormatTime(workTime);
        AwayTimeText.Text = FormatTime(awayTime);
        StartTimeText.Text = startTime.ToString("HH:mm");
        EndTimeText.Text = endTime.ToString("HH:mm");
        ModeText.Text = isWorkMode ? "В работе" : "Дрейфую";
        
        var sessionCount = dataService.GetSessionCountByDate(DateTime.Today, isWorkMode);
        SessionCountText.Text = sessionCount.ToString();
        
        Owner = Application.Current.MainWindow;
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
