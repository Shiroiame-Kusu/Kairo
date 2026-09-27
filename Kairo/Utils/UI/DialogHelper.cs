using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;

namespace Kairo.Utils
{
    /// <summary>
    /// 统一的确认对话框（用于删除、退出登录等不可轻易撤销的操作）
    /// </summary>
    internal static class DialogHelper
    {
        private static bool _isShowing;

        /// <param name="destructive">危险操作默认聚焦「取消」，避免误按回车</param>
        public static async Task<bool> ConfirmAsync(Window? owner, string title, string message, string primaryText, bool destructive = false)
        {
            owner ??= Access.DashBoard ?? Access.MainWindow;
            if (owner == null || _isShowing) return false;

            var dialog = new FAContentDialog
            {
                Title = title,
                Content = new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 420
                },
                PrimaryButtonText = primaryText,
                CloseButtonText = "取消",
                DefaultButton = destructive ? FAContentDialogButton.Close : FAContentDialogButton.Primary
            };

            _isShowing = true;
            try
            {
                return await dialog.ShowAsync(owner) == FAContentDialogResult.Primary;
            }
            catch (Exception ex)
            {
                AppLogger.Exception("显示确认对话框失败", ex);
                return false;
            }
            finally
            {
                _isShowing = false;
            }
        }
    }
}
