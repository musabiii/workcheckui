using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WorkCheck.Services;

public sealed class TrayIconService : IDisposable
{
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);

    private readonly NotifyIcon _notifyIcon;
    private IntPtr _lastHIcon;
    private bool _disposed;

    private static readonly Color Green = Color.FromArgb(0x40, 0x98, 0x3E);
    private static readonly Color Yellow = Color.FromArgb(0xF9, 0xE2, 0xAF);
    private static readonly Color Red = Color.FromArgb(0xF3, 0x8B, 0xA8);
    private static readonly Color Gray = Color.FromArgb(0x6C, 0x70, 0x86);
    private static readonly Color TextColor = Color.White;

    public TrayIconService(Action onExit, Action onToggleWindow)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Показать / Скрыть", null, (_, _) => onToggleWindow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Закрыть WorkCheck", null, (_, _) => onExit());

        _notifyIcon = new NotifyIcon
        {
            Visible = true,
            Text = "WorkCheck",
            ContextMenuStrip = menu
        };

        _notifyIcon.DoubleClick += (_, _) => onToggleWindow();

        RenderIcon(0, Gray, TextColor);
    }

    public void Update(int minutes, TimeSpan session, TimeSpan pomodoroTime, TimeSpan pomodoro2Time,
        bool isDrifting = false)
    {
        if (_disposed) return;

        Color color;
        if (isDrifting)
        {
            color = Gray;
        }
        else if (session >= pomodoro2Time)
            color = Red;
        else if (session >= pomodoroTime)
            color = Yellow;
        else
            color = Green;

        RenderIcon(minutes, color, TextColor);

        var tip = isDrifting
            ? $"WorkCheck — дрейфую ({minutes} мин)"
            : $"WorkCheck — сессия {minutes} мин";
        _notifyIcon.Text = tip.Length > 63 ? tip[..63] : tip;
    }

    private void RenderIcon(int minutes, Color background, Color foreground)
    {
        const int sz = 16;
        using var bmp = new Bitmap(sz, sz);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            using var bgBrush = new SolidBrush(background);
            FillRoundedRect(g, bgBrush, new Rectangle(0, 0, sz, sz), 4);

            string text = Math.Min(minutes, 99).ToString();
            float fontSize = text.Length >= 2 ? 8.5f : 11f;

            using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(foreground);

            var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };

            g.DrawString(text, font, brush, new RectangleF(0, 0, sz, sz + 1), sf);
        }

        var hIcon = bmp.GetHicon();
        _notifyIcon.Icon = Icon.FromHandle(hIcon);

        if (_lastHIcon != IntPtr.Zero)
            DestroyIcon(_lastHIcon);
        _lastHIcon = hIcon;
    }

    private static void FillRoundedRect(Graphics g, Brush brush, Rectangle rect, int radius)
    {
        using var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        g.FillPath(brush, path);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();

        if (_lastHIcon != IntPtr.Zero)
            DestroyIcon(_lastHIcon);
    }
}
