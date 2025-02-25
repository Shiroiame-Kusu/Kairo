using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using Wpf.Ui.Common;
using Wpf.Ui.Controls;
using Newtonsoft.Json;
using Kairo.Extensions;

namespace Kairo.Utils
{
    internal static class Logger
    {
        public static void Output(LogType type, params object?[] objects)
        {
            StringBuilder bld = new();
            foreach (var o in objects)
            {
                if (o != null)
                {
                    if (o is Exception e)
                    {
                        bld.Append(Environment.NewLine + CrashInterception.MergeException(e));
                    }
                    else
                    {
                        bld.Append(o + " ");
                    }
                }
                else if (type == LogType.Debug || type == LogType.DetailDebug)
                {
                    bld.Append("null ");
                }
            }
            string line = bld.ToString();
            if (type != LogType.Info)
            {
                line = line.TrimEnd(' ');
            }
            if(type == LogType.Info)
            {
                Console.WriteLine(line);
            }
            if (type == LogType.Debug || type == LogType.DetailDebug)
            {
                Console.WriteLine(line);
                return;
            }
            if (type == LogType.Error) {
                Console.WriteLine("[Error] " + line); return;
            }
            try { Access.Status.Dispatcher.Invoke(() => Access.Status.Append(LogPreProcess.Color(type, line))); } catch (Exception e) { }
            LogPreProcess.Process.Cache.Add(new(type, line));
            if (LogPreProcess.Process.Cache.Count > 200)
            {
                LogPreProcess.Process.Cache.RemoveRange(0, LogPreProcess.Process.Cache.Count - 300);
            }
        }

        public static bool MsgBox(string text, string caption, int buttons, int icon, int ClassName)
        {

            //Logger.Output(LogType.DetailDebug, new object[] { text, caption, buttons, icon }.ToJson());
            if (buttons == 0)
            {
                if (text.Contains("\n"))
                {
                        if(ClassName == 0)
                        {
                            Access.MainWindow?.OpenSnackbar(
                            text.Split('\n')[0],
                            text.Substring(text.IndexOf('\n')).TrimStart('\n'),
                            icon == 48 ? SymbolRegular.Warning24 : SymbolRegular.Dismiss24
                            );
                        }
                        else
                        {
                            Access.DashBoard?.OpenSnackbar(
                            text.Split('\n')[0],
                            text.Substring(text.IndexOf('\n')).TrimStart('\n'),
                            icon == 48 ? SymbolRegular.Warning24 : SymbolRegular.Dismiss24
                            );
                        }
                        
                }
                
                
                else
                {
                    if (ClassName == 0)
                    {
                        Access.MainWindow?.OpenSnackbar(
                        "执行失败",
                        text,
                        icon == 48 ? SymbolRegular.Warning24 : SymbolRegular.Dismiss24
                        );
                    }
                    else
                    {
                        Access.DashBoard?.OpenSnackbar(
                        "执行失败",
                        text,
                        icon == 48 ? SymbolRegular.Warning24 : SymbolRegular.Dismiss24
                        );
                    }
                        
                }
                return true;
            }
            bool confirmed = false;
            MessageBox messageBox = new()
            {
                Title = caption,
                Content = new TextBlock
                {
                    Text = text,
                    TextWrapping = System.Windows.TextWrapping.WrapWithOverflow
                },
                ShowInTaskbar = false,
                ResizeMode = System.Windows.ResizeMode.NoResize,
                Topmost = true,
                Width = 350,
                ButtonLeftName = buttons <= 1 ? "确定" : "是",
                ButtonRightName = buttons <= 1 ? "取消" : "否"
            };
            messageBox.ButtonRightClick += (_, _) => messageBox.Close();
            messageBox.ButtonLeftClick += (_, _) =>
            {
                confirmed = true;
                messageBox.Close();
            };
            messageBox.ShowDialog();
            return confirmed;
        }

    }
}
