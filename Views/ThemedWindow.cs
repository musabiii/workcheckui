using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using WorkCheck.Helpers;
using Border = System.Windows.Controls.Border;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;

namespace WorkCheck.Views;

// Base for every regular window of the app. Its template (App.xaml) draws the themed card: background,
// outline, corners and shadow, an optional title bar with a close button, and the resize grip. It also
// makes the window draggable, reserves the transparent space the shadow needs, and switches to the
// system backdrop (WindowBackdrop) for themes that have one. A new window only declares its content:
//
//   <views:ThemedWindow x:Class="..." Title="Отчёт" ShowHeader="True" Width="400" Height="300"> ... </views:ThemedWindow>
//
// Width and Height are the size of the visible card; the shadow space is added on top.
public class ThemedWindow : Window
{
    public static readonly DependencyProperty SurfaceProperty = DependencyProperty.Register(
        nameof(Surface), typeof(Brush), typeof(ThemedWindow));

    public static readonly DependencyProperty ShadowProperty = DependencyProperty.Register(
        nameof(Shadow), typeof(Effect), typeof(ThemedWindow));

    public static readonly DependencyProperty ShowHeaderProperty = DependencyProperty.Register(
        nameof(ShowHeader), typeof(bool), typeof(ThemedWindow));

    private bool _hasBackdrop;

    public ThemedWindow()
    {
        // Implicit styles are looked up by the exact type, so subclasses ask for this one explicitly
        SetResourceReference(StyleProperty, typeof(ThemedWindow));
    }

    // Card background; BaseBrush by default
    public Brush? Surface
    {
        get => (Brush?)GetValue(SurfaceProperty);
        set => SetValue(SurfaceProperty, value);
    }

    // Card shadow; ShadowMd by default
    public Effect? Shadow
    {
        get => (Effect?)GetValue(ShadowProperty);
        set => SetValue(ShadowProperty, value);
    }

    // Title bar with the window Title and a close button
    public bool ShowHeader
    {
        get => (bool)GetValue(ShowHeaderProperty);
        set => SetValue(ShowHeaderProperty, value);
    }

    // Windows that live for the whole session (status, compact) stay layered: their look follows live theme
    // switches, which a window that already chose the system backdrop could not
    public bool UsesSystemBackdrop { get; set; } = true;

    public bool IsDraggable { get; set; } = true;

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);

        _hasBackdrop = UsesSystemBackdrop && WindowBackdrop.TryEnable(this);
        if (!_hasBackdrop)
            AddShadowSpace();
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (_hasBackdrop && GetTemplateChild("PART_Card") is Border card)
            WindowBackdrop.StyleCard(card);

        if (GetTemplateChild("PART_Close") is Button close)
            close.Click += (_, _) => OnHeaderClose();
    }

    // The close button of the title bar; dialogs with a cancel action can route it there
    protected virtual void OnHeaderClose() => Close();

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!IsDraggable || e.ButtonState != MouseButtonState.Pressed) return;

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // The button was released before the drag started
        }
    }

    private void AddShadowSpace()
    {
        var space = 2 * AppThemes.WindowShadowSpace;
        if (!double.IsNaN(Width)) Width += space;
        if (!double.IsNaN(Height)) Height += space;
        if (MinWidth > 0) MinWidth += space;
        if (MinHeight > 0) MinHeight += space;
    }
}
