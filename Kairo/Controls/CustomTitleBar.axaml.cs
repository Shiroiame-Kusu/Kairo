using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace Kairo.Controls;

public partial class CustomTitleBar : UserControl
{
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<CustomTitleBar, string?>(nameof(Title));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    private Window? _window;
    private ContentControl? _maxIcon;

    public CustomTitleBar()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, TitleBarPointerPressed, RoutingStrategies.Tunnel);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _maxIcon = this.FindControl<ContentControl>("MaxIcon");
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _window = this.GetVisualRoot() as Window;
        if (_window != null)
        {
            _window.PropertyChanged += WindowOnPropertyChanged;
            UpdateMaxIcon();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_window != null)
        {
            _window.PropertyChanged -= WindowOnPropertyChanged;
        }
        _window = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void WindowOnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty)
        {
            UpdateMaxIcon();
        }
    }

    private static bool IsFromButton(PointerPressedEventArgs e)
    {
        if (e.Source is Control control)
        {
            // If the original source is the Button itself or inside a Button (e.g., Close/Max/Min), don't start a drag or toggle maximize.
            return control is Button || control.FindAncestorOfType<Button>() != null;
        }
        return false;
    }

    private void TitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_window == null)
            return;

        // Ignore presses that originate on interactive buttons to avoid hijacking their clicks
        if (IsFromButton(e))
            return;

        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsLeftButtonPressed)
        {
            if (e.ClickCount == 2)
            {
                ToggleMaximize();
            }
            else
            {
                try { _window.BeginMoveDrag(e); } catch { /* ignore */ }
            }
        }
    }

    private void Minimize_Click(object? sender, RoutedEventArgs e)
    {
        if (_window != null)
            _window.WindowState = WindowState.Minimized;
    }

    private void MaxRestore_Click(object? sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }

    private void ToggleMaximize()
    {
        if (_window == null) return;
        _window.WindowState = _window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        UpdateMaxIcon();
    }

    private void UpdateMaxIcon()
    {
        if (_window == null || _maxIcon == null) return;
        _maxIcon.Content = _window.WindowState == WindowState.Maximized ? "❐" : "▢"; // simple glyphs
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        _window?.Close();
    }
}
