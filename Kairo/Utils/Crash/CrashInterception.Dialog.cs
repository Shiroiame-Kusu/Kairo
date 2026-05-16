using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Kairo.Utils
{
    internal static partial class CrashInterception
    {
        private static async void ShowCrashDialog(CrashReport report, Window? parentWindow)
        {
            try
            {
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    var window = BuildCrashWindow(report);
                    try
                    {
                        if (parentWindow != null)
                            await window.ShowDialog(parentWindow);
                        else if (Access.MainWindow != null)
                            await window.ShowDialog(Access.MainWindow);
                        else
                            window.Show();
                    }
                    catch (System.Exception ex)
                    {
                        AppLogger.Exception("Unhandled exception in Kairo/Utils/Crash/CrashInterception.Dialog.cs:34", ex);
                        window.Show();
                    }
                });
            }
            catch (Exception ex)
            {
                AppLogger.Exception("Unhandled exception in Kairo/Utils/Crash/CrashInterception.Dialog.cs:37", ex);
                Debug.WriteLine(ex);
            }
        }

        private static Window BuildCrashWindow(CrashReport report)
        {
            var tabs = new TabControl();
            var items = new List<TabItem>();

            items.Add(new TabItem
            {
                Header = "概览",
                Content = new ScrollViewer
                {
                    Content = new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap,
                        Text = BuildOverviewText(report)
                    }
                }
            });

            if (CurrentOptions.ShowExceptionTab)
            {
                items.Add(new TabItem
                {
                    Header = "异常",
                    Content = new ScrollViewer
                    {
                        Content = new TextBlock
                        {
                            FontFamily = new FontFamily("Consolas,JetBrains Mono,monospace"),
                            TextWrapping = TextWrapping.Wrap,
                            Text = report.ExceptionMerged
                        }
                    }
                });
            }

            if (CurrentOptions.ShowLogsTab && report.RecentLogs.Count > 0)
            {
                var sb = new StringBuilder();
                foreach (var l in report.RecentLogs)
                    sb.AppendLine(l.Type + " | " + l.Text);
                items.Add(new TabItem
                {
                    Header = "日志",
                    Content = new ScrollViewer
                    {
                        Content = new TextBlock
                        {
                            FontFamily = new FontFamily("Consolas,JetBrains Mono,monospace"),
                            TextWrapping = TextWrapping.Wrap,
                            Text = sb.ToString()
                        }
                    }
                });
            }

            if (CurrentOptions.ShowJsonTab)
            {
                string json;
                try { json = SerializeCrashReport(report, indented: true); }
                catch (System.Exception ex)
                {
                    AppLogger.Exception("Unhandled exception in Kairo/Utils/Crash/CrashInterception.Dialog.cs:102", ex);
                    json = "<json serialization failed>";
                }
                items.Add(new TabItem
                {
                    Header = "JSON",
                    Content = new ScrollViewer
                    {
                        Content = new TextBlock
                        {
                            FontFamily = new FontFamily("Consolas,JetBrains Mono,monospace"),
                            TextWrapping = TextWrapping.Wrap,
                            Text = json
                        }
                    }
                });
            }

            foreach (var it in items) tabs.Items.Add(it);

            var btnCopy = new Button { Content = "复制", Width = 80 };
            var btnFolder = new Button { Content = "打开目录", Width = 90 };
            var btnClose = new Button { Content = "关闭", Width = 80 };

            btnCopy.Click += async (_, _) =>
            {
                try
                {
                    var txt = BuildHumanReadable(report);
                    if (btnCopy.GetVisualRoot() is Window w)
                        await (w.Clipboard?.SetTextAsync(txt) ?? Task.CompletedTask);
                }
                catch (Exception copyEx)
                {
                    AppLogger.Exception("Unhandled exception in Kairo/Utils/Crash/CrashInterception.Dialog.cs:132", copyEx);
                    Debug.WriteLine(copyEx);
                }
            };

            btnFolder.Click += (_, _) =>
            {
                try
                {
                    var dir = CurrentOptions.CrashDirectory;
                    if (Directory.Exists(dir))
                    {
                        try
                        {
                            using var _ = Process.Start(new ProcessStartInfo
                            {
                                FileName = dir,
                                UseShellExecute = true
                            });
                        }
                        catch (Exception startEx)
                        {
                            AppLogger.Exception("Unhandled exception in Kairo/Utils/Crash/CrashInterception.Dialog.cs:153", startEx);
                            Debug.WriteLine(startEx);
                        }
                    }
                }
                catch (Exception folderEx)
                {
                    AppLogger.Exception("Unhandled exception in Kairo/Utils/Crash/CrashInterception.Dialog.cs:159", folderEx);
                    Debug.WriteLine(folderEx);
                }
            };

            btnClose.Click += (_, _) =>
            {
                if (btnClose.GetVisualRoot() is Window w) w.Close();
            };

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Children = { btnCopy, btnFolder, btnClose }
            };

            var grid = new Grid
            {
                RowDefinitions = new RowDefinitions("*,Auto"),
                Margin = new Thickness(10)
            };
            Grid.SetRow(tabs, 0);
            Grid.SetRow(buttonPanel, 1);
            grid.Children.Add(tabs);
            grid.Children.Add(buttonPanel);

            return new Window
            {
                Title = "Kairo - 崩溃报告 (" + report.Id + ")",
                Width = 760,
                Height = 560,
                CanResize = true,
                Content = grid
            };
        }

        private static string BuildOverviewText(CrashReport report)
        {
            var sb = new StringBuilder();
            sb.AppendLine("发生了未处理的异常，已生成崩溃报告。");
            sb.AppendLine();
            sb.AppendLine($"ID: {report.Id}");
            sb.AppendLine($"时间: {report.Time:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"版本: {report.Version} - {report.Branch}.{report.Revision}");
            sb.AppendLine($"运行时: .NET {Environment.Version}");
            sb.AppendLine($"运行时框架: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
            sb.AppendLine($"系统: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
            sb.AppendLine($"进程架构: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
            sb.AppendLine($"Uptime: {report.Uptime}");
            sb.AppendLine($"异常: {report.ExceptionType}");
            sb.AppendLine($"消息: {report.Message}");
            sb.AppendLine();
            sb.AppendLine($"◦ 崩溃文件保存在 {CurrentOptions.CrashDirectory}");
            sb.AppendLine("◦ 点击 复制 以复制完整报告，可粘贴到 Issue");
            sb.AppendLine("◦ 点击 打开目录 直达日志文件夹");
            sb.AppendLine();
            sb.AppendLine("提交 Issue: https://github.com/Shiroiame-Kusu/Kairo/issues/new?assignees=&labels=%E2%9D%97+%E5%B4%A9%E6%BA%83&template=crash_report.yml&title=崩溃反馈+" + Uri.EscapeDataString(report.ExceptionType));
            return sb.ToString();
        }
    }
}
