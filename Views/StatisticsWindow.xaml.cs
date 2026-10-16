using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WorkCheck.Helpers;
using WorkCheck.Models;
using WorkCheck.Services;
using Binding = System.Windows.Data.Binding;

namespace WorkCheck.Views;

public record PeriodRow(string Name, string[] Cells, string Total, bool IsTotal = false);

public partial class StatisticsWindow : Window
{
    private enum Period { Day, Week, Month }

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

    private Period CurrentPeriod =>
        WeekPeriodRadio.IsChecked == true ? Period.Week
        : MonthPeriodRadio.IsChecked == true ? Period.Month
        : Period.Day;

    private static DateTime WeekStart(DateTime date) =>
        date.Date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    private static (DateTime From, DateTime To) RangeOf(Period period, DateTime date) => period switch
    {
        Period.Week => (WeekStart(date), WeekStart(date).AddDays(7)),
        Period.Month => (new DateTime(date.Year, date.Month, 1), new DateTime(date.Year, date.Month, 1).AddMonths(1)),
        _ => (date.Date, date.Date.AddDays(1))
    };

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        DragMove();
    }

    private void LoadStatistics()
    {
        var period = CurrentPeriod;
        var (from, to) = RangeOf(period, _currentDate);

        DateText.Text = period switch
        {
            Period.Week => $"{from:dd.MM} – {to.AddDays(-1):dd.MM.yyyy}",
            Period.Month => Russian.TextInfo.ToTitleCase(Russian.DateTimeFormat.GetMonthName(from.Month)) + $" {from:yyyy}",
            _ => from.ToString("dd.MM.yyyy")
        };

        var showsToday = DateTime.Today >= from && DateTime.Today < to;
        TodayButton.IsEnabled = !showsToday;
        TodayButton.Opacity = showsToday ? 0.4 : 1;

        var minDuration = AllSessionsRadio.IsChecked == true ? TimeSpan.FromMinutes(1) : FullSessionDuration;
        var allSessions = _dataService.GetSessionsInRange(from, to);
        var sessions = allSessions.Where(s => s.IsWorkMode && s.Duration >= minDuration).ToList();

        // Shown as their own rows under the projects; the totals above stay work-only
        var stored = _dataService.GetAwayPeriodsInRange(from, to, isWorkMode: true);
        var extras = new List<ExtraRow>
        {
            new("Перерыв", "Teal", CleanAway(stored, AwayKind.Break, allSessions, from, to)),
            new("Не у компа", "Red", CleanAway(stored, AwayKind.Away, allSessions, from, to)),
            new("Дрейф", "Overlay1", allSessions.Where(s => !s.IsWorkMode && s.Duration >= minDuration).ToList())
        };

        SessionCountText.Text = sessions.Count.ToString();
        TotalTimeText.Text = TimeFormatter.FormatShort(Total(sessions));

        var projectNames = _dataService.GetAllProjects().ToDictionary(p => p.Id, p => p.Name);
        string ProjectName(Session s) =>
            s.ProjectId is int id && projectNames.TryGetValue(id, out var name) ? name : "(удалён)";
        int ColorKey(Session s) =>
            s.ProjectId is int id && projectNames.ContainsKey(id) ? id : -1;

        if (period == Period.Day)
            LoadDay(sessions, extras, ProjectName, ColorKey);
        else
            LoadPeriod(PeriodColumns(period, from, to), sessions, extras, ProjectName, ColorKey);

        SessionsGrid.Visibility = period == Period.Day ? Visibility.Visible : Visibility.Collapsed;
        PeriodGrid.Visibility = period == Period.Day ? Visibility.Collapsed : Visibility.Visible;
    }

    private void LoadDay(List<Session> sessions, List<ExtraRow> extras,
        Func<Session, string> projectName, Func<Session, int> colorKey)
    {
        SessionsGrid.ItemsSource = sessions.Select(s => new
        {
            s.StartTime,
            s.EndTime,
            DurationText = TimeFormatter.FormatShort(s.Duration),
            ProjectName = projectName(s),
            s.Description
        }).ToList();

        Gantt.Columns = [];
        Gantt.Bars = sessions
            .Select(s => new GanttBar(projectName(s), colorKey(s), s.StartTime, s.EndTime, s.Duration, SessionTip(s, projectName(s))))
            .Concat(extras.SelectMany((row, i) => row.Items.Select(s => new GanttBar(row.Name, -1, s.StartTime, s.EndTime,
                s.EndTime - s.StartTime, $"{row.Name} · {OwnerName(s, projectName)}\n{RangeText(s)}",
                ColorRole: row.Role, Group: i + 1))))
            .ToList();
    }

    // A row under the projects: breaks, time away or drift; Items carry the project they belong to
    private record ExtraRow(string Name, string Role, List<Session> Items);

    private static string OwnerName(Session s, Func<Session, string> projectName) =>
        s.ProjectId == null ? "без проекта" : projectName(s);

    // Cleaned periods of one kind, each tagged with its project: the stored one, or for older rows
    // (which have none) the project of the last session before it, since the project can't change while away
    private static List<Session> CleanAway(List<AwayPeriod> stored, AwayKind kind, List<Session> sessions,
        DateTime from, DateTime to)
    {
        var ofKind = stored.Where(p => p.Kind == kind).ToList();
        return TimeRanges.CleanAway(ofKind.Select(p => new TimeRange(p.Start, p.End)), sessions, from, to,
                TimeSpan.FromMinutes(1))
            .Select(r =>
            {
                var middle = r.Start + r.Duration / 2;
                var projectId = ofKind.FirstOrDefault(p => p.ProjectId != null && p.Start <= middle && p.End >= middle)?.ProjectId
                    ?? sessions.Where(s => s.EndTime <= r.Start.AddSeconds(1)).MaxBy(s => s.EndTime)?.ProjectId;
                return new Session
                {
                    StartTime = r.Start, EndTime = r.End, Duration = r.Duration, IsWorkMode = true, ProjectId = projectId
                };
            })
            .ToList();
    }

    private static string RangeText(Session s) =>
        $"{s.StartTime:HH:mm}–{s.EndTime:HH:mm} · {TimeFormatter.FormatShort(s.EndTime - s.StartTime)}";

    // Week: one column per day. Month: one column per Monday–Sunday week, clipped to the month.
    private static List<GanttColumn> PeriodColumns(Period period, DateTime from, DateTime to)
    {
        var columns = new List<GanttColumn>();
        for (var start = from; start < to;)
        {
            var end = period == Period.Week ? start.AddDays(1) : Min(WeekStart(start).AddDays(7), to);
            var label = period == Period.Week
                ? $"{DayName(start)} {start:dd}"
                : $"{start:dd}–{end.AddDays(-1):dd}";
            columns.Add(new GanttColumn(start, end, label));
            start = end;
        }
        return columns;
    }

    private void LoadPeriod(List<GanttColumn> columns, List<Session> sessions, List<ExtraRow> extras,
        Func<Session, string> projectName, Func<Session, int> colorKey)
    {
        int ColumnIndex(Session s) => columns.FindIndex(c => s.StartTime >= c.Start && s.StartTime < c.End);
        string[] Cells(IEnumerable<Session> group) =>
            columns.Select((_, i) => FormatCell(group.Where(s => ColumnIndex(s) == i))).ToArray();

        BuildPeriodColumns(columns);

        var rows = sessions
            .GroupBy(projectName)
            .OrderByDescending(Total)
            .Select(g => new PeriodRow(g.Key, Cells(g), TimeFormatter.FormatClock(Total(g))))
            .ToList();

        if (rows.Count > 0)
            rows.Add(new PeriodRow("Итого", Cells(sessions), TimeFormatter.FormatClock(Total(sessions)), IsTotal: true));
        foreach (var extra in extras.Where(e => e.Items.Count > 0))
            rows.Add(new PeriodRow(extra.Name, Cells(extra.Items), TimeFormatter.FormatClock(Total(extra.Items))));

        PeriodGrid.ItemsSource = rows;

        // One row per project, one cell per column (day or week) the project was worked on
        Gantt.Columns = columns;
        Gantt.Bars = sessions
            .GroupBy(s => (Project: projectName(s), Column: ColumnIndex(s)))
            .Select(g =>
            {
                var column = columns[g.Key.Column];
                var total = Total(g);
                var tip = $"{g.Key.Project}\n{ColumnTitle(column)} · {TimeFormatter.FormatShort(total)} · сессий: {g.Count()}";
                return new GanttBar(g.Key.Project, colorKey(g.First()), column.Start, column.End, total, tip,
                    TimeFormatter.FormatClock(total));
            })
            .Concat(extras.SelectMany((extra, i) => ExtraCells(extra, i + 1)))
            .ToList();

        // Drift and away get one cell per column like a project, in their own row under the projects
        IEnumerable<GanttBar> ExtraCells(ExtraRow extra, int group) => extra.Items
            .GroupBy(ColumnIndex)
            .Where(g => g.Key >= 0)
            .Select(g =>
            {
                var column = columns[g.Key];
                var total = Total(g);
                return new GanttBar(extra.Name, -1, column.Start, column.End, total,
                    $"{extra.Name}\n{ColumnTitle(column)} · {TimeFormatter.FormatShort(total)}", TimeFormatter.FormatClock(total),
                    extra.Role, group);
            });
    }

    private void BuildPeriodColumns(List<GanttColumn> columns)
    {
        PeriodGrid.Columns.Clear();
        PeriodGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Проект",
            Binding = new Binding(nameof(PeriodRow.Name)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            MinWidth = 110,
            ElementStyle = (Style)FindResource("TrimmedCell")
        });

        for (var i = 0; i < columns.Count; i++)
        {
            PeriodGrid.Columns.Add(new DataGridTextColumn
            {
                Header = columns[i].Label,
                Binding = new Binding($"{nameof(PeriodRow.Cells)}[{i}]"),
                Width = 68
            });
        }

        PeriodGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Итого",
            Binding = new Binding(nameof(PeriodRow.Total)),
            Width = 68
        });
    }

    private static TimeSpan Total(IEnumerable<Session> sessions) =>
        TimeSpan.FromTicks(sessions.Sum(s => s.Duration.Ticks));

    private static string FormatCell(IEnumerable<Session> sessions)
    {
        var total = Total(sessions);
        return total > TimeSpan.Zero ? TimeFormatter.FormatClock(total) : "";
    }

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    private static string DayName(DateTime day) => Russian.DateTimeFormat.GetAbbreviatedDayName(day.DayOfWeek);

    private static string ColumnTitle(GanttColumn column) =>
        column.End - column.Start == TimeSpan.FromDays(1)
            ? $"{DayName(column.Start)} {column.Start:dd.MM}"
            : $"{column.Start:dd.MM}–{column.End.AddDays(-1):dd.MM}";

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
        _currentDate = Step(-1);
        LoadStatistics();
    }

    private void OnNextClick(object sender, RoutedEventArgs e)
    {
        var next = Step(1);
        if (RangeOf(CurrentPeriod, next).From > DateTime.Today) return;

        _currentDate = Min(next, DateTime.Today);
        LoadStatistics();
    }

    private void OnTodayClick(object sender, RoutedEventArgs e)
    {
        _currentDate = DateTime.Today;
        LoadStatistics();
    }

    private DateTime Step(int direction) => CurrentPeriod switch
    {
        Period.Week => _currentDate.AddDays(7 * direction),
        Period.Month => _currentDate.AddMonths(direction),
        _ => _currentDate.AddDays(direction)
    };

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
