using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.VisualTree;
using Kairo.Components.DashBoard;
using Kairo.Utils;
using System;

namespace Kairo.Controls;

public partial class CustomTitleBar : UserControl
{
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<CustomTitleBar, string?>(nameof(Title));
    public static readonly StyledProperty<bool> ShowUserInfoProperty = AvaloniaProperty.Register<CustomTitleBar, bool>(nameof(ShowUserInfo), false);
    public static readonly StyledProperty<IImage?> IconSourceProperty = AvaloniaProperty.Register<CustomTitleBar, IImage?>(nameof(IconSource), ProviderBranding.GetIconImage(Global.CurrentProvider));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public bool ShowUserInfo
    {
        get => GetValue(ShowUserInfoProperty);
        set => SetValue(ShowUserInfoProperty, value);
    }

    public IImage? IconSource
    {
        get => GetValue(IconSourceProperty);
        set => SetValue(IconSourceProperty, value);
    }

    private Window? _window;
    private ContentControl? _maxIcon;
    private StackPanel? _userInfoPanel;
    private Image? _userAvatar;
    private TextBlock? _userNameText;

    public CustomTitleBar()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, TitleBarPointerPressed, RoutingStrategies.Bubble);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _maxIcon = this.FindControl<ContentControl>("MaxIcon");
        _userInfoPanel = this.FindControl<StackPanel>("UserInfoPanel");
        _userAvatar = this.FindControl<Image>("UserAvatar");
        _userNameText = this.FindControl<TextBlock>("UserNameText");
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window != null)
        {
            _window.PropertyChanged += WindowOnPropertyChanged;
        }
        UpdateMaxIcon();
        if (ShowUserInfo)
        {
            UpdateUserInfo();
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ShowUserInfoProperty)
        {
            if (_userInfoPanel != null)
            {
                _userInfoPanel.IsVisible = ShowUserInfo;
            }
            if (ShowUserInfo)
            {
                UpdateUserInfo();
            }
        }
    }

    private void UpdateUserInfo()
    {
        if (_userInfoPanel != null)
        {
            _userInfoPanel.IsVisible = true;
        }
        if (_userNameText != null)
        {
            _userNameText.Text = Global.Config.Username ?? string.Empty;
        }
        if (_userAvatar != null && DashBoard.Avatar != null)
        {
            _userAvatar.Source = DashBoard.Avatar;
        }
    }

    public void RefreshAvatar()
    {
        if (_userAvatar != null && DashBoard.Avatar != null)
        {
            _userAvatar.Source = DashBoard.Avatar;
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
        var window = GetWindow();
        if (window == null || IsFromButton(e)) return;

        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed) return;

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        try { window.BeginMoveDrag(e); }
        catch (Exception ex)
        {
            AppLogger.Exception("Unhandled exception in Kairo/Controls/CustomTitleBar.axaml.cs:159", ex);
        }
    }

    private void Minimize_Click(object? sender, RoutedEventArgs e)
    {
        if (GetWindow() is { } window)
            window.WindowState = WindowState.Minimized;
    }

    private void MaxRestore_Click(object? sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }

    private void ToggleMaximize()
    {
        var window = GetWindow();
        if (window == null) return;
        window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        UpdateMaxIcon();
    }

    private void UpdateMaxIcon()
    {
        if (_maxIcon == null) return;
        var window = GetWindow();
        _maxIcon.Content = window?.WindowState == WindowState.Maximized ? "❐" : "▢";
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        GetWindow()?.Close();
    }

    private Window? GetWindow() => _window ??= TopLevel.GetTopLevel(this) as Window;
}
