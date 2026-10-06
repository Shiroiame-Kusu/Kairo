using System;
using System.Collections.Generic;
using Avalonia.Threading;
using Kairo.Core.Localization;
using Kairo.ViewModels;

namespace Kairo.Localization;

/// <summary>
/// 切换语言时通知所有存活的视图模型刷新绑定，让在属性里拼出来的文本（数量、状态等）重新生成。
/// 只保存弱引用，不会让视图模型无法回收
/// </summary>
internal static class LanguageRefresh
{
    private static readonly List<WeakReference<ViewModelBase>> ViewModels = new();

    static LanguageRefresh()
    {
        Localizer.LanguageChanged += OnLanguageChanged;
    }

    public static void Track(ViewModelBase viewModel)
    {
        lock (ViewModels)
        {
            // 顺带清理已经回收的视图模型
            if (ViewModels.Count >= 64 && ViewModels.Count % 64 == 0)
                ViewModels.RemoveAll(reference => !reference.TryGetTarget(out _));
            ViewModels.Add(new WeakReference<ViewModelBase>(viewModel));
        }
    }

    private static void OnLanguageChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
            RefreshAll();
        else
            Dispatcher.UIThread.Post(RefreshAll);
    }

    private static void RefreshAll()
    {
        var alive = new List<ViewModelBase>();
        lock (ViewModels)
        {
            ViewModels.RemoveAll(reference => !reference.TryGetTarget(out _));
            foreach (var reference in ViewModels)
                if (reference.TryGetTarget(out var viewModel))
                    alive.Add(viewModel);
        }

        foreach (var viewModel in alive)
            viewModel.NotifyLanguageChanged();
    }
}
