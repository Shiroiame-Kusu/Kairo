using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using Ookii.Dialogs.Wpf;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Kairo.Utils
{
    internal static class CrashInterception
    {
        /// <summary>
        /// 初始化
        /// </summary>
        public static void Init()
        {
            AppDomain.CurrentDomain.UnhandledException += (_, e) => ShowException((Exception)e.ExceptionObject);
            TaskScheduler.UnobservedTaskException += (_, e) => ShowException(e.Exception);
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
            public static readonly object
                Crash = new();
        }
        /// <summary>
        /// 显示错误消息
        /// </summary>
        /// <param name="e">错误消息</param>
        public static void ShowException(Exception e)
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
                        + $"{Global.Version} - {Global.Branch}" + "  |  " +
                        "NET" + Environment.Version.ToString() +
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

            Ookii.Dialogs.Wpf.TaskDialog taskDialog = new()
            {
                Buttons = { new(ButtonType.Ok) },
                MainInstruction = "唔……发生了一点小问题(っ °Д °;)っ",
                WindowTitle = "LocyanFrp",
                Content = "" +
                    $"版本： {Global.Version} - {Global.Branch}\n" +
                    $"时间：{DateTime.Now}\n" +
                    $"NET版本：{Environment.Version}\n\n" +
                    $"◦ 崩溃日志已保存在 {Path.Combine("logs", "crash", $"{DateTime.Now:yyyy-MM-dd}.log")}\n" +
                    $"◦ 反馈此问题可以帮助作者更好的改进LocyanFrp",
                MainIcon = Ookii.Dialogs.Wpf.TaskDialogIcon.Error,
                Footer = $"你可以<a href=\"https://github.com/LoCyan-Team/Kairo/issues/new?assignees=&labels=%E2%9D%97+%E5%B4%A9%E6%BA%83&projects=&template=crash_report.yml&title=崩溃反馈+{e.GetType()}\">提交Issue</a>",
                FooterIcon = Ookii.Dialogs.Wpf.TaskDialogIcon.Information,
                EnableHyperlinks = true,
                ExpandedInformation = exceptionMsg
            };
            taskDialog.HyperlinkClicked += (_, e) => Process.Start(new ProcessStartInfo(e.Href) { UseShellExecute = true });
            taskDialog.ShowDialog();
            
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
