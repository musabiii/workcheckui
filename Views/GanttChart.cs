using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using WorkCheck.Helpers;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace WorkCheck.Views;

// ColorKey picks a stable palette color (e.g. project id); a negative key draws the bar gray.
// ColorRole, when set, overrides it with a theme role. Label, when set, is drawn on the bar if it fits.
// Rows with a higher Group go below the others (e.g. drift and away under the projects), after a divider.
// Inside a group, rows with Indent 0 come first and Indent 1 rows follow as their indented children,
// by Order and then by total; a row is identified by its Group and Row, so a name can repeat across groups.
public record GanttBar(string Row, int ColorKey, DateTime Start, DateTime End, TimeSpan Duration, string ToolTip,
    string? Label = null, string? ColorRole = null, int Group = 0, int Indent = 0, int Order = 0, bool Hidden = false);

// Hidden bars are not drawn; they only make their row exist (e.g. a parent row with nothing of its own)

public record GanttColumn(DateTime Start, DateTime End, string Label);

public class GanttChart : Canvas
{
    private const double LabelWidth = 205;
    private const double TotalWidth = 88;
    private const double HeaderHeight = 20;
    private const double RowHeight = 26;
    private const double BarHeight = 14;
    private const double CellBarHeight = 18;
    private const double CellBarInset = 3;
    private const double MinBarWidth = 2;
    private const double MinBarLabelWidth = 36;
    private const double MinHourLabelSpacing = 34;
    private const double IndentWidth = 16;

    private static readonly Brush GridBrush = Frozen(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
    private static Brush StripeBrush => AppThemes.Brush("Crust");
    private static Brush LabelBrush => AppThemes.Brush("Subtext1");
    private static Brush TotalBrush => AppThemes.Brush("Text");
    private static Brush HourBrush => AppThemes.Brush("Overlay0");
    private static Brush BarTextBrush => AppThemes.Brush("OnAccent");
    private static Brush DividerBrush => AppThemes.Brush("Surface2");
    private static Brush ChildLabelBrush => AppThemes.Brush("Overlay1");

    // Project colors by Id % 7. Green belongs to the "За компьютером" row, so its slot is Blue:
    // replacing it rather than removing it keeps every other project on the color it already had.
    private static readonly string[] PaletteRoles = ["Blue", "Blue", "Yellow", "Mauve", "Peach", "Teal", "Red"];

    public static readonly DependencyProperty BarsProperty = DependencyProperty.Register(
        nameof(Bars), typeof(IReadOnlyList<GanttBar>), typeof(GanttChart),
        new PropertyMetadata(Array.Empty<GanttBar>(), OnChanged));

    public IReadOnlyList<GanttBar> Bars
    {
        get => (IReadOnlyList<GanttBar>)GetValue(BarsProperty);
        set => SetValue(BarsProperty, value);
    }

    // When set, the axis is split into these labelled columns (e.g. days of a week) instead of an hour scale
    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
        nameof(Columns), typeof(IReadOnlyList<GanttColumn>), typeof(GanttChart),
        new PropertyMetadata(Array.Empty<GanttColumn>(), OnChanged));

    public IReadOnlyList<GanttColumn> Columns
    {
        get => (IReadOnlyList<GanttColumn>)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    // Parent rows (rows with indented children) start folded; this holds the names of the ones the user
    // opened, shared with the owner so its table unfolds the same rows. ExpandedChanged fires after a click.
    public ISet<string> Expanded { get; set; } = new HashSet<string>();
    public event Action? ExpandedChanged;

    public const string ExpandedMark = "  ▾";
    public const string CollapsedMark = "  ▸";

    // Zoom on the hour scale: drag over the timeline to zoom in, Ctrl+wheel zooms around the cursor,
    // Shift+wheel pans, a double click or "весь день" resets. A new day (a different full range) resets too.
    private static readonly TimeSpan MinZoomSpan = TimeSpan.FromMinutes(10);
    private static readonly int[] StepMinutes = [5, 10, 15, 30, 60, 120, 180, 240, 360];
    private (DateTime From, DateTime To)? _zoom;
    private (DateTime From, DateTime To) _fullRange;
    private (DateTime From, DateTime To) _view;
    private double? _dragStartX;
    private Rectangle? _selection;

    private bool CanZoom => Columns.Count == 0 && Bars.Count > 0;

    // When set, the hour scale shows exactly this span instead of fitting the bars (e.g. a plan from now on)
    private (DateTime From, DateTime To)? _fixedRange;
    public (DateTime From, DateTime To)? FixedRange
    {
        get => _fixedRange;
        set
        {
            _fixedRange = value;
            Rebuild();
        }
    }

    public bool ShowZoomHint { get; set; } = true;

    public GanttChart()
    {
        ClipToBounds = true;
        // Transparent, not null, so empty space still receives the mouse for selecting a range
        Background = System.Windows.Media.Brushes.Transparent;
        MouseLeftButtonDown += OnZoomMouseDown;
        MouseMove += OnZoomMouseMove;
        MouseLeftButtonUp += OnZoomMouseUp;
        MouseWheel += OnZoomWheel;
        SizeChanged += (_, e) =>
        {
            if (e.WidthChanged) Rebuild();
        };
    }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((GanttChart)d).Rebuild();

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private void Rebuild()
    {
        Children.Clear();

        var rows = Bars
            .GroupBy(b => (b.Group, b.Row))
            .Select(g => (Name: g.Key.Row, g.Key.Group, Indent: g.Max(b => b.Indent), Order: g.Min(b => b.Order),
                Total: TimeSpan.FromTicks(g.Sum(b => b.Duration.Ticks))))
            .OrderBy(r => r.Group)
            .ThenBy(r => r.Indent)
            .ThenBy(r => r.Order)
            .ThenByDescending(r => r.Total)
            .ToList();

        // A group with indented rows has its plain row as their parent
        var parents = rows.Where(r => r.Indent > 0).Select(r => r.Group).ToHashSet();
        string? ParentName(int group) => parents.Contains(group) ? rows.First(r => r.Group == group && r.Indent == 0).Name : null;
        rows = rows
            .Where(r => r.Indent == 0 || ParentName(r.Group) is not { } parent || Expanded.Contains(parent))
            .ToList();

        Height = HeaderHeight + rows.Count * RowHeight;
        if (rows.Count == 0 || ActualWidth <= LabelWidth) return;

        var cellScale = Columns.Count > 0;
        var full = AxisRange();
        if (cellScale || full != _fullRange) _zoom = null;
        _fullRange = full;
        var (from, to) = _view = _zoom ?? full;

        var chartWidth = ActualWidth - LabelWidth;
        var totalHours = (to - from).TotalHours;
        double X(DateTime t) => LabelWidth + (t - from).TotalHours / totalHours * chartWidth;

        for (var i = 0; i < rows.Count; i++)
        {
            var top = HeaderHeight + i * RowHeight;
            if (i % 2 == 1)
                Add(new Rectangle { Width = ActualWidth, Height = RowHeight, Fill = StripeBrush }, 0, top);

            if (i > 0 && rows[i].Group != rows[i - 1].Group)
                Add(new Rectangle { Width = ActualWidth, Height = 1, Fill = DividerBrush }, 0, top);

            var indent = rows[i].Indent * IndentWidth;
            var name = rows[i].Name;
            var isParent = rows[i].Indent == 0 && parents.Contains(rows[i].Group);
            var label = new TextBlock
            {
                Text = isParent ? name + (Expanded.Contains(name) ? ExpandedMark : CollapsedMark) : name,
                Foreground = rows[i].Indent > 0 ? ChildLabelBrush : LabelBrush,
                FontSize = 12,
                Width = LabelWidth - TotalWidth - 12 - indent,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = name
            };
            if (isParent)
            {
                label.Cursor = System.Windows.Input.Cursors.Hand;
                label.Background = System.Windows.Media.Brushes.Transparent;
                label.MouseLeftButtonDown += (_, e) =>
                {
                    // Handled, so the window does not start dragging
                    e.Handled = true;
                    if (!Expanded.Remove(name)) Expanded.Add(name);
                    Rebuild();
                    ExpandedChanged?.Invoke();
                };
            }
            Add(label, indent, top + 5);

            Add(new TextBlock
            {
                Text = TimeFormatter.FormatShort(rows[i].Total),
                Foreground = TotalBrush,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Width = TotalWidth,
                TextAlignment = TextAlignment.Right
            }, LabelWidth - TotalWidth - 8, top + 5);
        }

        if (cellScale)
            DrawColumns(X);
        else
        {
            DrawHourScale(from, to, X, chartWidth / totalHours);
            DrawZoomInfo();
        }

        var barHeight = cellScale ? CellBarHeight : BarHeight;
        var inset = cellScale ? CellBarInset : 0;

        foreach (var bar in Bars)
        {
            var row = rows.FindIndex(r => r.Name == bar.Row && r.Group == bar.Group);
            if (row < 0 || bar.Hidden) continue;
            // Cut to the visible span so a zoomed-in chart does not draw over the labels
            var left = Math.Max(X(bar.Start), LabelWidth);
            var right = Math.Min(X(bar.End), ActualWidth);
            if (right < left) continue;
            var x = left + inset;
            var width = Math.Max(MinBarWidth, right - left - 2 * inset);
            var top = HeaderHeight + row * RowHeight + (RowHeight - barHeight) / 2;

            var rect = new Rectangle
            {
                Width = width,
                Height = barHeight,
                RadiusX = 3,
                RadiusY = 3,
                Fill = bar.ColorRole != null ? AppThemes.Brush(bar.ColorRole)
                    : bar.ColorKey < 0 ? HourBrush
                    : AppThemes.Brush(PaletteRoles[bar.ColorKey % PaletteRoles.Length]),
                ToolTip = bar.ToolTip,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            ToolTipService.SetInitialShowDelay(rect, 0);
            Add(rect, x, top);

            if (bar.Label != null && width >= MinBarLabelWidth)
            {
                Add(new TextBlock
                {
                    Text = bar.Label,
                    Foreground = BarTextBrush,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Width = width,
                    TextAlignment = TextAlignment.Center,
                    IsHitTestVisible = false
                }, x, top + (barHeight - 15) / 2);
            }
        }
    }

    private (DateTime From, DateTime To) AxisRange()
    {
        if (Columns.Count > 0)
            return (Columns[0].Start, Columns[^1].End);
        if (FixedRange is { } fixedRange)
            return fixedRange;

        var first = Bars.Min(b => b.Start);
        var last = Bars.Max(b => b.End);
        var from = new DateTime(first.Year, first.Month, first.Day, first.Hour, 0, 0);
        var to = new DateTime(last.Year, last.Month, last.Day, last.Hour, 0, 0);
        if (to < last) to = to.AddHours(1);
        if (to <= from) to = from.AddHours(1);
        return (from, to);
    }

    // Labels every 5 minutes to 6 hours, whichever first leaves room; grid lines at most an hour apart
    private void DrawHourScale(DateTime from, DateTime to, Func<DateTime, double> x, double pixelsPerHour)
    {
        var labelStep = StepMinutes.FirstOrDefault(m => m * pixelsPerHour / 60 >= MinHourLabelSpacing, StepMinutes[^1]);
        var gridStep = Math.Min(labelStep, 60);

        var first = from.Date.AddMinutes(Math.Ceiling((from - from.Date).TotalMinutes / gridStep) * gridStep);
        for (var t = first; t <= to; t = t.AddMinutes(gridStep))
        {
            DrawGridLine(x(t));
            if ((int)(t - t.Date).TotalMinutes % labelStep == 0 && t < to)
                Add(new TextBlock { Text = t.ToString("HH:mm"), Foreground = HourBrush, FontSize = 10 }, x(t) + 3, 2);
        }
    }

    // Top left corner, above the labels: the shown span with a reset link, or a hint
    private void DrawZoomInfo()
    {
        if (_zoom is not { } zoom)
        {
            if (ShowZoomHint)
                Add(new TextBlock { Text = "выделите отрезок, чтобы приблизить", Foreground = HourBrush, FontSize = 10 }, 0, 2);
            return;
        }

        var info = new TextBlock { FontSize = 10, Cursor = Cursors.Hand, Background = System.Windows.Media.Brushes.Transparent };
        info.Inlines.Add(new System.Windows.Documents.Run($"{zoom.From:HH:mm}–{zoom.To:HH:mm}  ") { Foreground = LabelBrush });
        info.Inlines.Add(new System.Windows.Documents.Run("⟲ весь день") { Foreground = AppThemes.Brush("Blue") });
        info.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            ResetZoom();
        };
        Add(info, 0, 2);
    }

    private void ResetZoom()
    {
        _zoom = null;
        Rebuild();
    }

    private DateTime TimeAt(double x)
    {
        var chartWidth = ActualWidth - LabelWidth;
        var fraction = Math.Clamp((x - LabelWidth) / chartWidth, 0, 1);
        return _view.From + TimeSpan.FromTicks((long)((_view.To - _view.From).Ticks * fraction));
    }

    // Keeps the span inside the full range and at least MinZoomSpan long; the full span means no zoom
    private void SetZoom(DateTime from, DateTime to)
    {
        var fullSpan = _fullRange.To - _fullRange.From;
        var span = to - from;
        if (span >= fullSpan)
        {
            ResetZoom();
            return;
        }
        if (span < MinZoomSpan)
        {
            var middle = from + span / 2;
            span = MinZoomSpan;
            from = middle - span / 2;
        }
        if (from < _fullRange.From) from = _fullRange.From;
        if (from + span > _fullRange.To) from = _fullRange.To - span;
        _zoom = (from, from + span);
        Rebuild();
    }

    private void OnZoomMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!CanZoom) return;
        var x = e.GetPosition(this).X;
        if (x <= LabelWidth) return;

        // Handled, so the window does not start dragging
        e.Handled = true;
        if (e.ClickCount == 2)
        {
            ResetZoom();
            return;
        }

        _dragStartX = x;
        _selection = new Rectangle
        {
            Height = ActualHeight,
            Width = 0,
            Fill = new SolidColorBrush(AppThemes.Color("Blue")) { Opacity = 0.2 },
            IsHitTestVisible = false
        };
        Add(_selection, x, 0);
        CaptureMouse();
    }

    private void OnZoomMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStartX is not { } start || _selection == null) return;
        var x = Math.Clamp(e.GetPosition(this).X, LabelWidth, ActualWidth);
        SetLeft(_selection, Math.Min(start, x));
        _selection.Width = Math.Abs(x - start);
    }

    private void OnZoomMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragStartX is not { } start) return;
        _dragStartX = null;
        ReleaseMouseCapture();
        if (_selection != null) Children.Remove(_selection);
        _selection = null;

        var x = Math.Clamp(e.GetPosition(this).X, LabelWidth, ActualWidth);
        if (Math.Abs(x - start) < 8) return;   // a click, not a selection
        SetZoom(TimeAt(Math.Min(start, x)), TimeAt(Math.Max(start, x)));
    }

    private void OnZoomWheel(object sender, MouseWheelEventArgs e)
    {
        if (!CanZoom) return;
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (!ctrl && !shift) return;   // a plain wheel scrolls the rows as before
        e.Handled = true;

        var (from, to) = _view;
        var span = to - from;
        if (ctrl)
        {
            var anchor = TimeAt(e.GetPosition(this).X);
            var factor = e.Delta > 0 ? 0.8 : 1.25;
            var newSpan = TimeSpan.FromTicks((long)(span.Ticks * factor));
            var newFrom = anchor - TimeSpan.FromTicks((long)((anchor - from).Ticks * factor));
            SetZoom(newFrom, newFrom + newSpan);
        }
        else if (_zoom != null)
        {
            var shift10 = TimeSpan.FromTicks(span.Ticks / 10) * (e.Delta > 0 ? -1 : 1);
            SetZoom(from + shift10, to + shift10);
        }
    }

    private void DrawColumns(Func<DateTime, double> x)
    {
        foreach (var column in Columns)
        {
            DrawGridLine(x(column.Start));
            Add(new TextBlock
            {
                Text = column.Label,
                Foreground = HourBrush,
                FontSize = 10,
                Width = x(column.End) - x(column.Start),
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            }, x(column.Start), 2);
        }
        DrawGridLine(x(Columns[^1].End));
    }

    private void DrawGridLine(double x) =>
        Add(new Rectangle { Width = 1, Height = Height - HeaderHeight + 4, Fill = GridBrush }, x, HeaderHeight - 4);

    private void Add(UIElement element, double left, double top)
    {
        SetLeft(element, left);
        SetTop(element, top);
        Children.Add(element);
    }
}
