using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WorkCheck.Helpers;
using WorkCheck.Models;
using WorkCheck.Services;
using Binding = System.Windows.Data.Binding;
using Button = System.Windows.Controls.Button;

namespace WorkCheck.Views;

// ToggleKey is set on rows with indented children: clicking such a row folds them
public record PeriodRow(string Name, string[] Cells, string Total, bool IsTotal = false, string? ToggleKey = null);

public partial class StatisticsWindow : ThemedWindow
{
    private enum Period { Day, Week, Month }

    private static readonly TimeSpan FullSessionDuration = TimeSpan.FromMinutes(25);
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    private readonly DataService _dataService;
    private DateTime _currentDate;

    // currentWork is the work of the running session, which is not in the database until it ends
    public StatisticsWindow(DataService dataService, Func<TimeSpan> currentWork)
    {
        InitializeComponent();
        _dataService = dataService;
        _currentWork = currentWork;
        _currentDate = DateTime.Today;

        // "Now" moves while the window is open, so the plan is laid out again every minute
        PlanInput.Text = _lastPlan;
        PomodoroInput.Text = _lastPomodoro;
        PauseInput.Text = _lastPause;
        (_lastPlanIsTotal ? PlanTotalRadio : PlanMoreRadio).IsChecked = true;
        (_lastPlanIsManual ? PlanManualRadio : PlanAutoRadio).IsChecked = true;
        _planTimer.Tick += (_, _) => BuildPlan();
        _planTimer.Start();
        Closed += (_, _) => _planTimer.Stop();
        BuildPlan();

        // Read-only window: a click anywhere else closes it. Deactivated also fires while closing,
        // and Close() during closing throws
        Closing += (_, _) => _closing = true;
        Deactivated += (_, _) =>
        {
            if (!_closing) Close();
        };

        // Rows with children start folded; the ones opened are shared by the chart and the table
        // and stay open while the window is
        Gantt.Expanded = _expanded;
        Gantt.ExpandedChanged += LoadStatistics;
        PeriodGrid.MouseLeftButtonUp += OnPeriodRowClick;

        LoadStatistics();
    }

    private readonly HashSet<string> _expanded = [];

    private readonly Func<TimeSpan> _currentWork;
    private readonly System.Windows.Threading.DispatcherTimer _planTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private static readonly TimeSpan PlanWindow = TimeSpan.FromHours(6);

    // Kept between openings of the window
    private static string _lastPlan = "3";
    private static string _lastPomodoro = "25";
    private static string _lastPause = "5";
    private static bool _lastPlanIsTotal;
    private static bool _lastPlanIsManual;

    // The manual plan: blocks of work and break in minutes, in order, kept while the app runs. They are
    // laid out up to the target, so a lower target or more work done just cuts them shorter.
    private static readonly List<(int Work, int Pause)> ManualBlocks = [];

    // Whether the laid-out manual plan already fills the target; adding more is then ignored
    private bool _manualFull;

    private bool _closing;

    private void OnAddBlockClick(object sender, RoutedEventArgs e) =>
        AddBlock(Minutes(PomodoroInput.Text, 25, min: 0), Minutes(PauseInput.Text, 5, min: 0));

    private void OnAddPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string preset }) return;
        var parts = preset.Split('/');
        AddBlock(int.Parse(parts[0]), int.Parse(parts[1]));
    }

    private void AddBlock(int work, int pause)
    {
        if (_manualFull || work + pause == 0) return;
        ManualBlocks.Add((work, pause));
        BuildPlan();
    }

    private void OnRemoveBlockClick(object sender, RoutedEventArgs e)
    {
        if (ManualBlocks.Count == 0) return;
        ManualBlocks.RemoveAt(ManualBlocks.Count - 1);
        BuildPlan();
    }

    private void OnClearBlocksClick(object sender, RoutedEventArgs e)
    {
        ManualBlocks.Clear();
        BuildPlan();
    }

    private void OnPlanChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _lastPlan = PlanInput.Text;
        _lastPomodoro = PomodoroInput.Text;
        _lastPause = PauseInput.Text;
        _lastPlanIsTotal = PlanTotalRadio.IsChecked == true;
        _lastPlanIsManual = PlanManualRadio.IsChecked == true;
        BuildPlan();
    }

    // Lays out the work still to plan from now on, in pomodoros with breaks between them (breaks are not
    // work): automatically, or from the blocks added by hand, and shows when it is done
    private void BuildPlan()
    {
        var now = DateTime.Now;
        PlanChart.FixedRange = (now, now + PlanWindow);
        var manual = PlanManualRadio.IsChecked == true;
        ManualPanel.Visibility = manual ? Visibility.Visible : Visibility.Collapsed;
        _manualFull = false;

        if (ParsePlan(PlanInput.Text) is not { } target)
        {
            ShowPlan(now, [], "введите время, например 3 или 2:30", "Overlay1");
            return;
        }

        // "всего": the target is for the whole day, so what is already worked on projects today comes off
        var planned = target;
        var summary = "";
        if (PlanTotalRadio.IsChecked == true)
        {
            var done = _dataService.GetTotalWorkTimeByDate(DateTime.Today, isWorkMode: true) + _currentWork();
            summary = $"сделано {TimeFormatter.FormatShort(done)} из {TimeFormatter.FormatShort(target)}";
            planned = target - done;
            if (planned <= TimeSpan.Zero)
            {
                _manualFull = true;
                ShowPlan(now, [], summary + " · план выполнен", "Green");
                return;
            }
            // the manual line says "из <остаток>" itself
            summary += manual ? " · " : $", осталось {TimeFormatter.FormatShort(planned)} · ";
        }

        if (manual)
            BuildManualPlan(now, planned, summary);
        else
            BuildAutoPlan(now, planned, summary);
    }

    private void BuildAutoPlan(DateTime now, TimeSpan planned, string summary)
    {
        // Lengths for the plan only, independent of the tracker settings; bad input falls back to 25 / 5
        var pomodoro = TimeSpan.FromMinutes(Minutes(PomodoroInput.Text, 25, min: 1));
        var pause = TimeSpan.FromMinutes(Minutes(PauseInput.Text, 5, min: 0));
        var bars = new List<GanttBar>();
        var t = now;
        var left = planned;
        var count = 0;
        var breaks = TimeSpan.Zero;

        // A pomodoro already running in work mode: what is left of it comes first, or a break right away
        // if it has run its full length
        var running = _currentWork();
        var firstWork = pomodoro;
        var current = "";
        if (running > TimeSpan.Zero && running < pomodoro)
        {
            firstWork = pomodoro - running;
            current = $"текущий помидор: ещё {TimeFormatter.FormatShort(firstWork)} · ";
        }
        else if (running >= pomodoro && pause > TimeSpan.Zero)
        {
            bars.Add(BreakBar(t, pause, $"Перерыв: помидор уже идёт {TimeFormatter.FormatShort(running)}"));
            t += pause;
            breaks += pause;
            current = "сначала перерыв · ";
        }

        while (left > TimeSpan.Zero)
        {
            var length = count == 0 ? firstWork : pomodoro;
            var work = left < length ? left : length;
            count++;
            bars.Add(WorkBar(t, work, count == 1 && firstWork < pomodoro ? "Текущий помидор" : $"Помидор {count}"));
            t += work;
            left -= work;
            if (left <= TimeSpan.Zero || pause <= TimeSpan.Zero) continue;

            bars.Add(BreakBar(t, pause, "Перерыв"));
            t += pause;
            breaks += pause;
        }

        ShowPlan(now, bars, summary + current +
            $"закончу в {t:HH:mm} · помидоров: {count}, перерывы {TimeFormatter.FormatShort(breaks)}", "Text", end: t);
    }

    // The added blocks one after another from now, cut where the planned work is reached: the last
    // pomodoro is shortened and nothing after it is laid out
    private void BuildManualPlan(DateTime now, TimeSpan planned, string summary)
    {
        var bars = new List<GanttBar>();
        var t = now;
        var work = TimeSpan.Zero;
        var breaks = TimeSpan.Zero;
        for (var i = 0; i < ManualBlocks.Count && work < planned; i++)
        {
            var (w, p) = ManualBlocks[i];
            if (w > 0)
            {
                var length = TimeSpan.FromMinutes(w);
                if (length > planned - work) length = planned - work;
                bars.Add(WorkBar(t, length, $"Блок {i + 1}: {w}/{p}"));
                t += length;
                work += length;
                if (work >= planned) break;
            }
            if (p > 0)
            {
                var length = TimeSpan.FromMinutes(p);
                bars.Add(BreakBar(t, length, w == 0 ? "Обед / перерыв" : "Перерыв"));
                t += length;
                breaks += length;
            }
        }

        _manualFull = work >= planned;
        if (ManualBlocks.Count == 0)
        {
            ShowPlan(now, bars, summary + "добавляйте блоки кнопками, пока не наберётся нужное время", "Overlay1");
            return;
        }
        var end = t;

        var text = $"набрано {TimeFormatter.FormatShort(work)} из {TimeFormatter.FormatShort(planned)}" +
                   (_manualFull ? "" : $", не хватает {TimeFormatter.FormatShort(planned - work)}") +
                   $" · перерывы {TimeFormatter.FormatShort(breaks)} · закончу в {t:HH:mm}" +
                   (_manualFull ? " · время заполнено" : "");
        // Blue while the plan still falls short, red once it reaches the planned time
        ShowPlan(now, bars, summary + text, _manualFull ? "Green" : "Text", end, _manualFull ? "Red" : "Blue");
    }

    private static GanttBar WorkBar(DateTime start, TimeSpan length, string title) =>
        new("Работа", -1, start, start + length, length,
            $"{title}\n{start:HH:mm}–{start + length:HH:mm} · {TimeFormatter.FormatShort(length)}", ColorRole: "Blue");

    private static GanttBar BreakBar(DateTime start, TimeSpan length, string title) =>
        new("Перерыв", -1, start, start + length, length,
            $"{title}\n{start:HH:mm}–{start + length:HH:mm}", ColorRole: "Teal", Order: 1);

    // Both rows are always there (hidden placeholders when empty), so the empty plan still shows its scale.
    // end, when given, is marked with a line and its time, red by default.
    private void ShowPlan(DateTime now, List<GanttBar> bars, string text, string role, DateTime? end = null,
        string markerRole = "Red")
    {
        PlanChart.MarkerRole = markerRole;
        PlanChart.Marker = end;
        bars.Add(new GanttBar("Работа", -1, now, now, TimeSpan.Zero, "", Hidden: true));
        bars.Add(new GanttBar("Перерыв", -1, now, now, TimeSpan.Zero, "", Order: 1, Hidden: true));
        PlanChart.Bars = bars;
        PlanResult.Text = text;
        PlanResult.Foreground = AppThemes.Brush(role);
    }

    private static int Minutes(string text, int fallback, int min) =>
        int.TryParse(text.Trim(), out var value) && value >= min && value <= 240 ? value : fallback;

    // "3" or "2,5" are hours, "2:30" is hours and minutes, "90м" is minutes; up to a day
    private static TimeSpan? ParsePlan(string text)
    {
        var s = text.Trim().Replace(',', '.').ToLowerInvariant();
        TimeSpan? result = null;
        var hm = System.Text.RegularExpressions.Regex.Match(s, @"^(\d{1,2}):(\d{1,2})$");
        if (hm.Success)
            result = new TimeSpan(int.Parse(hm.Groups[1].Value), int.Parse(hm.Groups[2].Value), 0);
        else if ((s.EndsWith("м") || s.EndsWith("мин")) && double.TryParse(s.TrimEnd('м', 'и', 'н', ' ').Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes))
            result = TimeSpan.FromMinutes(minutes);
        else if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var hours))
            result = TimeSpan.FromHours(hours);

        return result is { } r && r > TimeSpan.Zero && r <= TimeSpan.FromHours(24) ? r : null;
    }

    private void OnPeriodRowClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;
        if (ItemsControl.ContainerFromElement(PeriodGrid, source) is not DataGridRow { Item: PeriodRow { ToggleKey: { } key } }) return;

        if (!_expanded.Remove(key)) _expanded.Add(key);
        LoadStatistics();
    }

    // Marks rows followed by indented children as foldable and drops the children of folded ones
    private List<PeriodRow> Fold(List<PeriodRow> rows)
    {
        var result = new List<PeriodRow>();
        var hide = false;
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Name.StartsWith(ChildIndent))
            {
                if (!hide) result.Add(rows[i]);
                continue;
            }

            var hasChildren = i + 1 < rows.Count && rows[i + 1].Name.StartsWith(ChildIndent);
            hide = hasChildren && !_expanded.Contains(rows[i].Name);
            result.Add(hasChildren
                ? rows[i] with
                {
                    Name = rows[i].Name + (hide ? GanttChart.CollapsedMark : GanttChart.ExpandedMark),
                    ToggleKey = rows[i].Name
                }
                : rows[i]);
        }
        return result;
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
        var drift = allSessions.Where(s => !s.IsWorkMode && s.Duration >= minDuration).ToList();
        var extras = new List<ExtraRow>
        {
            new("Перерыв", "Teal", CleanAway(stored, AwayKind.Break, allSessions, from, to), ByProject: true),
            new("Не у компа", "Red", CleanAway(stored, AwayKind.Away, allSessions, from, to), ByProject: true),
            new("Дрейф", "Overlay1", drift),
            new("За компьютером", "Green", AtComputer(sessions, drift), ShowProject: false)
        };

        SessionCountText.Text = sessions.Count.ToString();
        TotalTimeText.Text = TimeFormatter.FormatShort(Total(sessions));
        ShowProductivity(Total(sessions), Total(sessions) + Total(drift));

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

        var split = extras.Where(e => e.ByProject).ToList();
        var plain = extras.Where(e => !e.ByProject).ToList();
        var bars = new List<GanttBar>();

        GanttBar Bar(Session s, string row, string tip, int group, int indent = 0, int order = 0, string? role = null, int key = -1) =>
            new(row, key, s.StartTime, s.EndTime, s.Duration, tip, ColorRole: role, Group: group, Indent: indent, Order: order);
        string Tip(ExtraRow extra, Session s) => extra.ShowProject
            ? $"{extra.Name} · {OwnerName(s, projectName)}\n{RangeText(s)}"
            : $"{extra.Name}\n{RangeText(s)}";

        if (ProjectGroupingRadio.IsChecked == true)
        {
            // Each project with its breaks and time away as indented children
            var owners = OwnerOrder(sessions, split, projectName);
            for (var k = 0; k < owners.Count; k++)
            {
                var work = sessions.Where(s => OwnerName(s, projectName) == owners[k]).ToList();
                bars.AddRange(work.Select(s => new GanttBar(owners[k], colorKey(s), s.StartTime, s.EndTime, s.Duration,
                    SessionTip(s, projectName(s)), Group: k)));

                var children = split.SelectMany((extra, j) => extra.Items
                    .Where(s => OwnerName(s, projectName) == owners[k])
                    .Select(s => Bar(s, extra.Name, Tip(extra, s), k, 1, j, extra.Role))).ToList();
                if (work.Count == 0 && children.Count > 0)
                    bars.Add(children[0] with { Row = owners[k], Indent = 0, Order = 0, Duration = TimeSpan.Zero, Hidden = true });
                bars.AddRange(children);
            }
            bars.AddRange(plain.SelectMany((extra, i) => extra.Items.Select(s =>
                Bar(s, extra.Name, Tip(extra, s), owners.Count + i, role: extra.Role))));
        }
        else
        {
            // Работа, Перерыв, Не у компа with their projects as indented children, then drift
            foreach (var s in sessions)
            {
                var tip = SessionTip(s, projectName(s));
                bars.Add(Bar(s, WorkRow, tip, 0, key: colorKey(s)) with { Duration = s.Duration });
                bars.Add(Bar(s, OwnerName(s, projectName), tip, 0, 1, key: colorKey(s)) with { Duration = s.Duration });
            }
            for (var j = 0; j < split.Count; j++)
            {
                foreach (var s in split[j].Items)
                {
                    bars.Add(Bar(s, split[j].Name, Tip(split[j], s), 1 + j, role: split[j].Role));
                    bars.Add(Bar(s, OwnerName(s, projectName), Tip(split[j], s), 1 + j, 1, role: split[j].Role));
                }
            }
            bars.AddRange(plain.SelectMany((extra, i) => extra.Items.Select(s =>
                Bar(s, extra.Name, Tip(extra, s), 1 + split.Count + i, role: extra.Role))));
        }

        Gantt.Columns = [];
        Gantt.Bars = bars;
    }

    private const string WorkRow = "Работа";

    // Projects by work time, then those with only breaks or time away in the period
    private static List<string> OwnerOrder(List<Session> sessions, List<ExtraRow> split, Func<Session, string> projectName) =>
        sessions.GroupBy(s => OwnerName(s, projectName)).OrderByDescending(Total).Select(g => g.Key)
            .Concat(split.SelectMany(e => e.Items).GroupBy(s => OwnerName(s, projectName)).OrderByDescending(Total).Select(g => g.Key))
            .Distinct()
            .ToList();

    // A row under the projects: breaks, time away or drift; Items carry the project they belong to
    private record ExtraRow(string Name, string Role, List<Session> Items, bool ByProject = false, bool ShowProject = true);

    // Work on any project plus drift is all the time spent at the computer. Only one mode runs at a time,
    // so the two never overlap, and keeping the stored durations makes the total match Работа + Дрейф.
    private static List<Session> AtComputer(List<Session> work, List<Session> drift) =>
        work.Concat(drift)
            .Select(s => new Session { StartTime = s.StartTime, EndTime = s.EndTime, Duration = s.Duration })
            .OrderBy(s => s.StartTime)
            .ToList();

    // Child rows in the period table: the grid has no tree, so the indent is leading spaces
    private const string ChildIndent = "    ";

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
        PeriodRow Row(string name, List<Session> items, bool child = false, bool isTotal = false) =>
            new((child ? ChildIndent : "") + name, Cells(items), TimeFormatter.FormatClock(Total(items)), isTotal);
        List<Session> Of(IEnumerable<Session> items, string owner) =>
            items.Where(s => OwnerName(s, projectName) == owner).ToList();

        // One bar per column (day or week) that has time, with the total as its label
        IEnumerable<GanttBar> CellsOf(IEnumerable<Session> items, string row, string title, int group,
            int indent = 0, int order = 0, string? role = null, int key = -1) => items
            .GroupBy(ColumnIndex)
            .Where(g => g.Key >= 0)
            .Select(g =>
            {
                var column = columns[g.Key];
                var total = Total(g);
                return new GanttBar(row, key, column.Start, column.End, total,
                    $"{title}\n{ColumnTitle(column)} · {TimeFormatter.FormatShort(total)}", TimeFormatter.FormatClock(total),
                    role, group, indent, order);
            });

        BuildPeriodColumns(columns);

        var split = extras.Where(e => e.ByProject).ToList();
        var plain = extras.Where(e => !e.ByProject).ToList();
        var rows = new List<PeriodRow>();
        var bars = new List<GanttBar>();

        if (ProjectGroupingRadio.IsChecked == true)
        {
            // Each project, then its breaks and time away indented under it
            var owners = OwnerOrder(sessions, split, projectName);
            for (var k = 0; k < owners.Count; k++)
            {
                var owner = owners[k];
                var work = Of(sessions, owner);
                rows.Add(Row(owner, work));
                bars.AddRange(CellsOf(work, owner, owner, k, key: work.Count > 0 ? colorKey(work[0]) : -1));

                var childBars = new List<GanttBar>();
                for (var j = 0; j < split.Count; j++)
                {
                    var items = Of(split[j].Items, owner);
                    if (items.Count == 0) continue;
                    rows.Add(Row(split[j].Name, items, child: true));
                    childBars.AddRange(CellsOf(items, split[j].Name, $"{owner} · {split[j].Name}", k, 1, j, split[j].Role));
                }
                if (work.Count == 0 && childBars.Count > 0)
                    bars.Add(childBars[0] with { Row = owner, Indent = 0, Order = 0, Duration = TimeSpan.Zero, Hidden = true });
                bars.AddRange(childBars);
            }
            if (sessions.Count > 0)
                rows.Add(Row("Итого", sessions, isTotal: true));

            for (var i = 0; i < plain.Count; i++)
            {
                if (plain[i].Items.Count == 0) continue;
                rows.Add(Row(plain[i].Name, plain[i].Items));
                bars.AddRange(CellsOf(plain[i].Items, plain[i].Name, plain[i].Name, owners.Count + i, role: plain[i].Role));
            }
        }
        else
        {
            // Работа, Перерыв, Не у компа with their projects indented under them, then drift
            var kinds = new List<(string Name, string? Role, List<Session> Items)> { (WorkRow, WorkRole, sessions) };
            kinds.AddRange(split.Select(e => (e.Name, (string?)e.Role, e.Items)));
            kinds.AddRange(plain.Select(e => (e.Name, (string?)e.Role, e.Items)));

            for (var g = 0; g < kinds.Count; g++)
            {
                var (name, role, items) = kinds[g];
                if (items.Count == 0) continue;

                rows.Add(Row(name, items));
                bars.AddRange(CellsOf(items, name, name, g, role: role));
                if (g > split.Count) continue;   // drift has no project breakdown

                var isWork = g == 0;
                foreach (var owner in items.GroupBy(s => OwnerName(s, projectName)).OrderByDescending(Total))
                {
                    rows.Add(Row(owner.Key, owner.ToList(), child: true));
                    bars.AddRange(CellsOf(owner, owner.Key, isWork ? owner.Key : $"{name} · {owner.Key}", g, 1,
                        role: isWork ? null : role, key: isWork ? colorKey(owner.First()) : -1));
                }
            }
        }

        PeriodGrid.ItemsSource = Fold(rows);
        Gantt.Columns = columns;
        Gantt.Bars = bars;
    }

    // Work cells summed over all projects have no single project color
    private const string WorkRole = "Subtext0";

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

    // Work on projects as a share of all time at the computer (work + drift)
    private void ShowProductivity(TimeSpan work, TimeSpan atComputer)
    {
        if (atComputer <= TimeSpan.Zero)
        {
            ProductivityValue.Text = "—";
            ProductivityValue.Foreground = AppThemes.Brush("Overlay1");
            ProductivityDetail.Text = "";
            return;
        }

        var percent = (int)Math.Round(work.TotalMinutes / atComputer.TotalMinutes * 100);
        ProductivityValue.Text = $"{percent}%";
        ProductivityValue.Foreground = AppThemes.Brush(percent >= 70 ? "Green" : percent >= 40 ? "Yellow" : "Red");
        ProductivityDetail.Text =
            $"  ·  {TimeFormatter.FormatShort(work)} работы из {TimeFormatter.FormatShort(atComputer)} за компьютером";
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

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
