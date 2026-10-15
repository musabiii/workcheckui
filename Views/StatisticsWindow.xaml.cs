using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WorkCheck.Helpers;
using WorkCheck.Models;
using WorkCheck.Services;
using Binding = System.Windows.Data.Binding;

namespace WorkCheck.Views;

public record WeekRow(string Name, string[] Days, string Total, bool IsTotal = false);

public partial class StatisticsWindow : Window
{
    private static readonly TimeSpan FullSessionDuration = TimeSpan.FromMinutes(25);
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    private readonly DataService _dataService;
    private DateTime _currentDate;

    public StatisticsWindow(DataService dataService)
    {
        InitializeComponent();
        _dataService = dataService;
        _currentDate = DateTime.Today;
        LoadStatistics();
    }

    private bool IsWeek => DayPeriodRadio.IsChecked != true;

    private static DateTime WeekStart(DateTime date) =>
        date.Date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        DragMove();
    }

    private void LoadStatistics()
    {
        var from = IsWeek ? WeekStart(_currentDate) : _currentDate.Date;
        var to = from.AddDays(IsWeek ? 7 : 1);

        DateText.Text = IsWeek
            ? $"{from:dd.MM} – {to.AddDays(-1):dd.MM.yyyy}"
            : from.ToString("dd.MM.yyyy");

        var minDuration = AllSessionsRadio.IsChecked == true ? TimeSpan.FromMinutes(1) : FullSessionDuration;
        var sessions = _dataService.GetSessionsInRange(from, to)
            .Where(s => s.IsWorkMode && s.Duration >= minDuration)
            .ToList();

        SessionCountText.Text = sessions.Count.ToString();
        TotalTimeText.Text = TimeFormatter.FormatShort(TimeSpan.FromTicks(sessions.Sum(s => s.Duration.Ticks)));

        var projectNames = _dataService.GetAllProjects().ToDictionary(p => p.Id, p => p.Name);
        string ProjectName(Session s) =>
            s.ProjectId is int id && projectNames.TryGetValue(id, out var name) ? name : "(удалён)";
        int ColorKey(Session s) =>
            s.ProjectId is int id && projectNames.ContainsKey(id) ? id : -1;

        if (IsWeek)
            LoadWeek(from, sessions, ProjectName, ColorKey);
        else
            LoadDay(sessions, ProjectName, ColorKey);

        SessionsGrid.Visibility = IsWeek ? Visibility.Collapsed : Visibility.Visible;
        WeekGrid.Visibility = IsWeek ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LoadDay(List<Session> sessions, Func<Session, string> projectName, Func<Session, int> colorKey)
    {
        SessionsGrid.ItemsSource = sessions.Select(s => new
        {
            s.StartTime,
            s.EndTime,
            DurationText = TimeFormatter.FormatShort(s.Duration),
            ProjectName = projectName(s),
            s.Description
        }).ToList();

        Gantt.AxisStart = null;
        Gantt.AxisEnd = null;
        Gantt.Bars = sessions.Select(s => new GanttBar(
            projectName(s), colorKey(s), s.StartTime, s.EndTime, s.Duration, SessionTip(s, projectName(s)))).ToList();
    }

    private void LoadWeek(DateTime weekStart, List<Session> sessions,
        Func<Session, string> projectName, Func<Session, int> colorKey)
    {
        var days = Enumerable.Range(0, 7).Select(i => weekStart.AddDays(i)).ToList();
        int DayIndex(Session s) => (s.StartTime.Date - weekStart).Days;

        BuildWeekColumns(days);

        var rows = sessions
            .GroupBy(projectName)
            .Select(g => (Name: g.Key, Sessions: g.ToList(), Total: TimeSpan.FromTicks(g.Sum(s => s.Duration.Ticks))))
            .OrderByDescending(p => p.Total)
            .Select(p => new WeekRow(
                p.Name,
                days.Select((_, i) => FormatCell(p.Sessions.Where(s => DayIndex(s) == i))).ToArray(),
                TimeFormatter.FormatClock(p.Total)))
            .ToList();

        if (rows.Count > 0)
        {
            rows.Add(new WeekRow(
                "Итого",
                days.Select((_, i) => FormatCell(sessions.Where(s => DayIndex(s) == i))).ToArray(),
                TimeFormatter.FormatClock(TimeSpan.FromTicks(sessions.Sum(s => s.Duration.Ticks))),
                IsTotal: true));
        }

        WeekGrid.ItemsSource = rows;

        // One row per project, one cell per day the project was worked on
        Gantt.AxisStart = weekStart;
        Gantt.AxisEnd = weekStart.AddDays(7);
        Gantt.Bars = sessions
            .GroupBy(s => (Project: projectName(s), Day: DayIndex(s)))
            .Select(g =>
            {
                var day = days[g.Key.Day];
                var total = TimeSpan.FromTicks(g.Sum(s => s.Duration.Ticks));
                var tip = $"{g.Key.Project}\n{DayLabel(day)} · {TimeFormatter.FormatShort(total)} · сессий: {g.Count()}";
                return new GanttBar(g.Key.Project, colorKey(g.First()), day, day.AddDays(1), total, tip,
                    TimeFormatter.FormatClock(total));
            })
            .ToList();
    }

    private void BuildWeekColumns(List<DateTime> days)
    {
        WeekGrid.Columns.Clear();
        WeekGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Проект",
            Binding = new Binding(nameof(WeekRow.Name)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            MinWidth = 110,
            ElementStyle = (Style)FindResource("TrimmedCell")
        });

        for (var i = 0; i < days.Count; i++)
        {
            WeekGrid.Columns.Add(new DataGridTextColumn
            {
                Header = $"{DayName(days[i])} {days[i]:dd}",
                Binding = new Binding($"{nameof(WeekRow.Days)}[{i}]"),
                Width = 62
            });
        }

        WeekGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Итого",
            Binding = new Binding(nameof(WeekRow.Total)),
            Width = 64
        });
    }

    private static string FormatCell(IEnumerable<Session> sessions)
    {
        var total = TimeSpan.FromTicks(sessions.Sum(s => s.Duration.Ticks));
        return total > TimeSpan.Zero ? TimeFormatter.FormatClock(total) : "";
    }

    private static string DayName(DateTime day) => Russian.DateTimeFormat.GetAbbreviatedDayName(day.DayOfWeek);

    private static string DayLabel(DateTime day) => $"{DayName(day)} {day:dd.MM}";

    private static string SessionTip(Session s, string projectName)
    {
        var tip = $"{projectName}\n{s.StartTime:HH:mm}–{s.EndTime:HH:mm} · {TimeFormatter.FormatShort(s.Duration)}";
        if (!string.IsNullOrWhiteSpace(s.Description))
            tip += $"\n{s.Description}";
        return tip;
    }

    private void OnViewChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        var showList = ListViewRadio.IsChecked == true;
        ListView.Visibility = showList ? Visibility.Visible : Visibility.Collapsed;
        GanttView.Visibility = showList ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        // Checked fires while InitializeComponent sets the defaults; the constructor loads the data itself
        if (!IsLoaded) return;
        LoadStatistics();
    }

    private void OnPreviousClick(object sender, RoutedEventArgs e)
    {
        _currentDate = _currentDate.AddDays(IsWeek ? -7 : -1);
        LoadStatistics();
    }

    private void OnNextClick(object sender, RoutedEventArgs e)
    {
        var next = _currentDate.AddDays(IsWeek ? 7 : 1);
        if (IsWeek ? WeekStart(next) > DateTime.Today : next > DateTime.Today) return;

        _currentDate = next > DateTime.Today ? DateTime.Today : next;
        LoadStatistics();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
