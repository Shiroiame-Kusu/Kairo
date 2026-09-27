using System;
using Avalonia.Data;
using Avalonia.Data.Core;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions.CompiledBindings;
using Avalonia.Metadata;
using Kairo.Core.Localization;

namespace Kairo.Localization;

/// <summary>
/// 界面文本：<c>Text="{l:Loc tunnels.title}"</c>。切换语言后自动更新。
/// 生成的是不依赖反射的编译绑定，可以在 Native AOT 下使用
/// </summary>
public sealed class LocExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string key)
    {
        Key = key;
    }

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public BindingBase ProvideValue(IServiceProvider serviceProvider) => Create(Key);

    /// <summary>在代码中为属性绑定界面文本，例如 <c>window.Bind(Window.TitleProperty, LocExtension.Create("app.title"))</c></summary>
    public static BindingBase Create(string key)
    {
        var property = new ClrPropertyInfo(key, _ => Localizer.Get(key), null, typeof(string));
        var path = new CompiledBindingPathBuilder()
            .Property(property, PropertyInfoAccessorFactory.CreateInpcPropertyAccessor)
            .Build();
        return new CompiledBinding(path)
        {
            Source = LocalizationSource.Instance,
            Mode = BindingMode.OneWay
        };
    }
}
