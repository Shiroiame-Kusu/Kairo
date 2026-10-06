using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Kairo.Core.Localization;
using Kairo.Localization;

namespace Kairo.Controls;

/// <summary>界面语言下拉框：选择后立即切换并保存，其他地方修改语言时同步显示</summary>
public partial class LanguageSelector : UserControl
{
    private readonly ComboBox _selector;
    private bool _syncing;

    public LanguageSelector()
    {
        AvaloniaXamlLoader.Load(this);
        _selector = this.FindControl<ComboBox>("Selector")!;
        _selector.ItemsSource = LanguageOption.All;
        SyncSelection();
        _selector.SelectionChanged += OnSelectionChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        Localizer.LanguageChanged += SyncSelection;
        SyncSelection();
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e) => Localizer.LanguageChanged -= SyncSelection;

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _selector.SelectedItem is not LanguageOption option) return;
        LanguageSettings.Change(option.Setting);
    }

    private void SyncSelection()
    {
        _syncing = true;
        try { _selector.SelectedItem = LanguageOption.Find(LanguageSettings.Current); }
        finally { _syncing = false; }
    }
}
