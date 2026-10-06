using System.ComponentModel;
using System.Runtime.CompilerServices;
using Kairo.Localization;

namespace Kairo.ViewModels
{
    /// <summary>
    /// Minimal base class for MVVM view models.
    /// </summary>
    public abstract class ViewModelBase : INotifyPropertyChanged
    {
        protected ViewModelBase()
        {
            LanguageRefresh.Track(this);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// 界面语言切换后调用。默认刷新全部绑定，属性里用 <c>L.T</c> 生成的文本会重新读取；
        /// 保存在字段里的文本需要在重写中重新生成
        /// </summary>
        protected virtual void OnLanguageChanged() => OnPropertyChanged(string.Empty);

        internal void NotifyLanguageChanged() => OnLanguageChanged();
    }
}
