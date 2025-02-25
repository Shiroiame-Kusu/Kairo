using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.IO;
using Kairo.Utils;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Threading;
using System.Security;
using System.Runtime.ConstrainedExecution;
using System.Diagnostics;
using System.Security.Policy;
using System.Text.RegularExpressions;
using System.Text;
using Kairo.Components.OAuth;

namespace Kairo
{
    /// <summary>
    /// App.xaml 的交互逻辑
    /// </summary>
    
    public partial class App : Application
    {
        private static string? Username = null;
        private static string? Password = null;
        private static bool DebugMode = Global.Config.DebugMode;
        public static bool TokenMode = false;
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool AllocConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool FreeConsole();

        
        
        protected override void OnStartup(StartupEventArgs e)
        {
            CrashInterception.Init();
            ConfigManager.Init();
            
            DispatcherUnhandledException += CurrentDomain_UnhandledException;
            DispatcherUnhandledException += (_, e) => CrashInterception.ShowException(e.Exception);
            
            
            // 处理启动参数

            string[] args = e.Args;
            OAuthCallbackHandler.Init();
            ProcessStartupParameters(args);
            base.OnStartup(e);
            

        }
        protected override void OnExit(ExitEventArgs e)
        {
            //Cef.Shutdown();
            base.OnExit(e);
        }
        private static void CurrentDomain_UnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;
        }
        private static void ProcessStartupParameters(string[] args)
        {
            int UsernameNum = 0;
            int PasswordNum = 0;
            if (args.Length > 0)
            {

                string pattern = @"^locyanfrp://([^/]+)/(\d+)$";
                Regex regex = new Regex(pattern);

                for (int j = 0; j < args.Count(); j++)
                {
                    if (args[j] == "--user" || args[j] == "--User" || args[j] == "--Username" || args[j] == "--username")
                    {
                        UsernameNum = j;
                    }
                    else if (args[j] == "--password" || args[j] == "--Password")
                    {
                        PasswordNum = j;
                    }
                    else if (args[j] == "--debug")
                    {
                        DebugMode = true;
                    }
                    else
                    {   
                        string url = args[j];
                        Match match = regex.Match(url);
                        if (match.Success) {
                            Console.WriteLine($"Received URL: {url}");
                            string[] parsedParameters = new string[]
                            {
                                match.Groups[1].Value,
                                match.Groups[2].Value
                            };
                            TokenMode = true;
                            DashBoard_Token dashBoard_Token = new DashBoard_Token(match.Groups[1].Value, int.Parse(match.Groups[2].Value));
                            dashBoard_Token.Show();

                        }
                    }
                }
                int Num = UsernameNum - PasswordNum;
                if (Num >= 2 && Num <= -2)
                {
                    Username = args[UsernameNum + 1];
                    Password = args[PasswordNum + 1];
                    if (Password != null && Username != null)
                    {
                        Global.Config.Username = Username;
                        foreach (char c in Password.ToCharArray())
                        {
                            Global.Password.AppendChar(c);
                        }

                        Global.LoginedByConsole = true;
                    }
                }

                if (DebugMode)
                {
                    AllocConsole(); // 打开控制台
                }
                else
                {
                    FreeConsole(); // 关闭控制台
                }
                // 解析和处理参数
                // 这里可以根据参数的内容执行不同的操作
                for (int x = 0; x < args.Count(); x++)
                {
                    if (x != args.Count() - 1)
                    {
                        Console.Write(args[x]);
                    }
                    else
                    {
                        Console.WriteLine(args[x]);
                    }
                }
            }
        }
    }
}
