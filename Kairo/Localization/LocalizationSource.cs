using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using Kairo.Core.Localization;

namespace Kairo.Localization;

/// <summary>
/// XAML 文本绑定的数据源：切换语言时发出一次“全部属性已变化”通知，所有 <see cref="LocExtension"/> 绑定随之刷新
/// </summary>
public sealed class LocalizationSource : INotifyPropertyChanged
{
    public static LocalizationSource Instance { get; } = new();

    private LocalizationSource()
    {
        Localizer.LanguageChanged += OnLanguageChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] => Localizer.Get(key);

    private void OnLanguageChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        else
            Dispatcher.UIThread.Post(() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty)));
    }

    /// <summary>注册本程序集的语言文件（Localization/Languages/*.json）</summary>
    [ModuleInitializer]
    internal static void RegisterLanguages() => Localizer.Register(typeof(LocalizationSource).Assembly, "Kairo.Lang.");
}
