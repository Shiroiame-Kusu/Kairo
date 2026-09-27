using Kairo.Core.Localization;
using Kairo.ViewModels;

namespace Kairo.Localization;

/// <summary>
/// 下拉框等列表中的一项文本。切换语言时只刷新显示文字，列表本身不变，
/// 避免 ItemsSource 被替换后 SelectedIndex 被重置
/// </summary>
public sealed class LocalizedOption : ViewModelBase
{
    private readonly string _key;

    public LocalizedOption(string key)
    {
        _key = key;
    }

    public string Text => L.T(_key);

    public override string ToString() => Text;
}
