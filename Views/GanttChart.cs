using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using WorkCheck.Helpers;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace WorkCheck.Views;

// ColorKey picks a stable palette color (e.g. project id); a negative key draws the bar gray.
// ColorRole, when set, overrides it with a theme role. Label, when set, is drawn on the bar if it fits.
// Rows with a higher Group go below the others (e.g. drift and away under the projects), after a divider.
// Inside a group, rows with Indent 0 come first and Indent 1 rows follow as their indented children,
// by Order and then by total; a row is identified by its Group and Row, so a name can repeat across groups.
public record GanttBar(string Row, int ColorKey, DateTime Start, DateTime End, TimeSpan Duration, string ToolTip,
    string? Label = null, string? ColorRole = null, int Group = 0, int Indent = 0, int Order = 0);

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

    private static readonly string[] PaletteRoles = ["Blue", "Green", "Yellow", "Mauve", "Peach", "Teal", "Red"];

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

    public GanttChart()
    {
        ClipToBounds = true;
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

        Height = HeaderHeight + rows.Count * RowHeight;
        if (rows.Count == 0 || ActualWidth <= LabelWidth) return;

        var (from, to) = AxisRange();
        var cellScale = Columns.Count > 0;

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
            Add(new TextBlock
            {
                Text = rows[i].Name,
                Foreground = rows[i].Indent > 0 ? ChildLabelBrush : LabelBrush,
                FontSize = 12,
                Width = LabelWidth - TotalWidth - 12 - indent,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = rows[i].Name
            }, indent, top + 5);

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
            DrawHourScale(from, to, X, chartWidth / totalHours);

        var barHeight = cellScale ? CellBarHeight : BarHeight;
        var inset = cellScale ? CellBarInset : 0;

        foreach (var bar in Bars)
        {
            var row = rows.FindIndex(r => r.Name == bar.Row && r.Group == bar.Group);
            var x = X(bar.Start) + inset;
            var width = Math.Max(MinBarWidth, X(bar.End) - X(bar.Start) - 2 * inset);
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

        var first = Bars.Min(b => b.Start);
        var last = Bars.Max(b => b.End);
        var from = new DateTime(first.Year, first.Month, first.Day, first.Hour, 0, 0);
        var to = new DateTime(last.Year, last.Month, last.Day, last.Hour, 0, 0);
        if (to < last) to = to.AddHours(1);
        if (to <= from) to = from.AddHours(1);
        return (from, to);
    }

    private void DrawHourScale(DateTime from, DateTime to, Func<DateTime, double> x, double pixelsPerHour)
    {
        var hourStep = Math.Max(1, (int)Math.Ceiling(MinHourLabelSpacing / pixelsPerHour));
        for (var t = from; t <= to; t = t.AddHours(1))
        {
            DrawGridLine(x(t));
            if ((t - from).TotalHours % hourStep == 0 && t < to)
                Add(new TextBlock { Text = t.ToString("HH:mm"), Foreground = HourBrush, FontSize = 10 }, x(t) + 3, 2);
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
