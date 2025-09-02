using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Kairo.Utils
{
    internal static class CrashInterception
    {
        /// <summary>
        /// 初始化
        /// </summary>
        public static void Init(Window? parentWindow = null)
        {
            AppDomain.CurrentDomain.UnhandledException += (_, e) => ShowException((Exception)e.ExceptionObject, parentWindow);
            TaskScheduler.UnobservedTaskException += (_, e) => ShowException(e.Exception, parentWindow);
        }

        public static void CreateDirectory(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }
        public static class FileLock
        {
            public static readonly object Crash = new();
        }

        /// <summary>
        /// 显示错误消息
        /// </summary>
        public static async void ShowException(Exception e, Window? parentWindow = null)
        {
            CreateDirectory(Path.Combine("logs", "crash"));
            string exceptionMsg = MergeException(e);
            try
            {
                lock (FileLock.Crash)
                {
                    File.AppendAllText(
                        Path.Combine("logs", "crash", $"{DateTime.Now:yyyy-MM-dd}.log"),
                        DateTime.Now + "  |  "
                        + $"{Global.Version} - {Global.Branch}.{Global.Revision}" + "  |  " +
                        "NET" + Environment.Version +
                        Environment.NewLine +
                        exceptionMsg +
                        Environment.NewLine + Environment.NewLine,
                        Encoding.UTF8
                    );
                }
            }
            catch (Exception exception)
            {
                Debug.WriteLine(exception);
            }

            var logPath = Path.Combine("logs", "crash", $"{DateTime.Now:yyyy-MM-dd}.log");
            var content = $"版本： {Global.Version} - {Global.Branch}\n" +
                          $"时间：{DateTime.Now}\n" +
                          $"NET版本：{Environment.Version}\n\n" +
                          $"◦ 崩溃日志已保存在 {logPath}\n" +
                          "◦ 反馈此问题可以帮助作者更好的改进Kairo\n\n" +
                          $"你可以提交Issue: https://github.com/Shiroiame-Kusu/Kairo/issues/new?assignees=&labels=%E2%9D%97+%E5%B4%A9%E6%BA%83&projects=&template=crash_report.yml&title=崩溃反馈+{e.GetType()}\n\n" +
                          $"详细信息:\n{exceptionMsg}";

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var textBlock = new TextBlock
                {
                    Text = content,
                    TextWrapping = TextWrapping.Wrap
                };
                var scroll = new ScrollViewer { Content = textBlock };
                var okButton = new Button { Content = "确定", Width = 80 };
                okButton.Click += (_, _) =>
                {
                    if (okButton.GetVisualRoot() is Window w) w.Close();
                };
                var buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 8, 0, 0),
                    Children = { okButton }
                };
                var grid = new Grid
                {
                    RowDefinitions = new RowDefinitions("*,Auto"),
                    Children = { scroll, buttonPanel }
                };
                Grid.SetRow(scroll, 0);
                Grid.SetRow(buttonPanel, 1);
                var dialog = new Window
                {
                    Title = "Kairo - 崩溃报告",
                    Width = 650,
                    Height = 500,
                    CanResize = true,
                    Content = grid
                };

                if (parentWindow != null)
                    await dialog.ShowDialog(parentWindow);
                else
                    dialog.Show();
            });
        }

        /// <summary>
        /// 合并错误信息
        /// </summary>
        /// <param name="e">错误信息</param>
        /// <returns>错误信息</returns>
        public static string MergeException(Exception? e)
        {
            string message = string.Empty;
            while (e != null)
            {
                message = e + Environment.NewLine + message;
                e = e?.InnerException;
            }
            return message;
        }
    }
}
