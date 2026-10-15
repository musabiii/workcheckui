using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using WorkCheck.Helpers;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace WorkCheck.Views;

// ColorKey picks a stable palette color (e.g. project id); a negative key draws the bar gray
public record GanttBar(string Row, int ColorKey, DateTime Start, DateTime End, TimeSpan Duration, string ToolTip);

public class GanttChart : Canvas
{
    private const double LabelWidth = 190;
    private const double TotalWidth = 72;
    private const double HeaderHeight = 20;
    private const double RowHeight = 26;
    private const double BarHeight = 14;
    private const double MinBarWidth = 2;
    private const double MinHourLabelSpacing = 34;

    private static readonly Brush GridBrush = Frozen(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
    private static readonly Brush StripeBrush = Frozen(Color.FromRgb(0x11, 0x11, 0x1A));
    private static readonly Brush LabelBrush = Frozen(Color.FromRgb(0xBA, 0xC2, 0xDE));
    private static readonly Brush TotalBrush = Frozen(Color.FromRgb(0xCD, 0xD6, 0xF4));
    private static readonly Brush HourBrush = Frozen(Color.FromRgb(0x6C, 0x70, 0x86));

    private static readonly Brush[] Palette =
    [
        Frozen(Color.FromRgb(0x89, 0xB4, 0xFA)),
        Frozen(Color.FromRgb(0xA6, 0xE3, 0xA1)),
        Frozen(Color.FromRgb(0xF9, 0xE2, 0xAF)),
        Frozen(Color.FromRgb(0xCB, 0xA6, 0xF7)),
        Frozen(Color.FromRgb(0xFA, 0xB3, 0x87)),
        Frozen(Color.FromRgb(0x94, 0xE2, 0xD5)),
        Frozen(Color.FromRgb(0xF3, 0x8B, 0xA8))
    ];

    public static readonly DependencyProperty BarsProperty = DependencyProperty.Register(
        nameof(Bars), typeof(IReadOnlyList<GanttBar>), typeof(GanttChart),
        new PropertyMetadata(Array.Empty<GanttBar>(), (d, _) => ((GanttChart)d).Rebuild()));

    public IReadOnlyList<GanttBar> Bars
    {
        get => (IReadOnlyList<GanttBar>)GetValue(BarsProperty);
        set => SetValue(BarsProperty, value);
    }

    public GanttChart()
    {
        ClipToBounds = true;
        SizeChanged += (_, e) =>
        {
            if (e.WidthChanged) Rebuild();
        };
    }

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
            .GroupBy(b => b.Row)
            .Select(g => (Name: g.Key, Total: TimeSpan.FromTicks(g.Sum(b => b.Duration.Ticks))))
            .OrderByDescending(r => r.Total)
            .ToList();

        Height = HeaderHeight + rows.Count * RowHeight;
        if (rows.Count == 0 || ActualWidth <= LabelWidth) return;

        var first = Bars.Min(b => b.Start);
        var last = Bars.Max(b => b.End);
        var from = new DateTime(first.Year, first.Month, first.Day, first.Hour, 0, 0);
        var to = new DateTime(last.Year, last.Month, last.Day, last.Hour, 0, 0);
        if (to < last) to = to.AddHours(1);
        if (to <= from) to = from.AddHours(1);

        var chartWidth = ActualWidth - LabelWidth;
        var totalHours = (to - from).TotalHours;
        double X(DateTime t) => LabelWidth + (t - from).TotalHours / totalHours * chartWidth;

        for (var i = 0; i < rows.Count; i++)
        {
            var top = HeaderHeight + i * RowHeight;
            if (i % 2 == 1)
                Add(new Rectangle { Width = ActualWidth, Height = RowHeight, Fill = StripeBrush }, 0, top);

            Add(new TextBlock
            {
                Text = rows[i].Name,
                Foreground = LabelBrush,
                FontSize = 12,
                Width = LabelWidth - TotalWidth - 12,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = rows[i].Name
            }, 0, top + 5);

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

        var hourStep = Math.Max(1, (int)Math.Ceiling(MinHourLabelSpacing / (chartWidth / totalHours)));
        for (var t = from; t <= to; t = t.AddHours(1))
        {
            var x = X(t);
            Add(new Rectangle { Width = 1, Height = Height - HeaderHeight + 4, Fill = GridBrush }, x, HeaderHeight - 4);

            if ((t - from).TotalHours % hourStep == 0 && t < to)
                Add(new TextBlock { Text = t.ToString("HH:mm"), Foreground = HourBrush, FontSize = 10 }, x + 3, 2);
        }

        foreach (var bar in Bars)
        {
            var row = rows.FindIndex(r => r.Name == bar.Row);
            var x = X(bar.Start);
            var width = Math.Max(MinBarWidth, X(bar.End) - x);

            var rect = new Rectangle
            {
                Width = width,
                Height = BarHeight,
                RadiusX = 3,
                RadiusY = 3,
                Fill = bar.ColorKey < 0 ? HourBrush : Palette[bar.ColorKey % Palette.Length],
                ToolTip = bar.ToolTip,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            ToolTipService.SetInitialShowDelay(rect, 0);
            Add(rect, x, HeaderHeight + row * RowHeight + (RowHeight - BarHeight) / 2);
        }
    }

    private void Add(UIElement element, double left, double top)
    {
        SetLeft(element, left);
        SetTop(element, top);
        Children.Add(element);
    }
}
