﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.IO;
using LoCyanFrpDesktop.Utils;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Threading;
using System.Security;
using CefSharp.Wpf;
using System.Runtime.ConstrainedExecution;
using CefSharp;

namespace LoCyanFrpDesktop
{
    /// <summary>
    /// App.xaml 的交互逻辑
    /// </summary>
    
    public partial class App : Application
    {
        public static string? Username = null;
        public static string? Password = null;
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool AllocConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool FreeConsole();

        
        
        protected override void OnStartup(StartupEventArgs e)
        {
            //bool openConsole = false;
            CrashInterception.Init();
            ConfigManager.Init();
            
            DispatcherUnhandledException += CurrentDomain_UnhandledException;
            DispatcherUnhandledException += (_, e) => CrashInterception.ShowException(e.Exception);
            int UsernameNum = 0;
            int PasswordNum = 0;
            bool DebugMode = Global.Config.DebugMode;
            //string Username;
            //string Password;
            // 处理启动参数
            string[] args = e.Args;
            /*Task.Run(() =>
            {
                while (true) {
                    using(ConfigManager ConfigManager = new(FileMode.Create))
                    {
                        Console.WriteLine("Saving...");
                    }
                    Thread.Sleep(1000);
                }
            });*/
            base.OnStartup(e);
            //MainWindow mainWindow = new();
            var settings = new CefSettings()
            {
                //By default CefSharp will use an in-memory cache, you need to specify a Cache Folder to persist data
                CachePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CefSharp\\Cache"),
                LogSeverity = LogSeverity.Verbose,
                LogFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs\\CEF.log")
            };

            Cef.Initialize(settings);
            if (args.Length > 0)
            {   
                int i = 0;
                for (int j = 0; j < args.Count(); j++) {
                    if (args[j] == "--user" || args[j] == "--User" || args[j] == "--Username" || args[j] == "--username")
                    {
                        UsernameNum = j;
                    }
                    else if(args[j] == "--password" || args[j] == "--Password")
                    {
                        PasswordNum = j;
                    }else if(args[j] == "--debug")
                    {
                        DebugMode = true;
                    }
                }
                int Num = UsernameNum - PasswordNum;
                if (Num >= 2 && Num <= -2) {
                    Username = args[UsernameNum + 1];
                    Password = args[PasswordNum + 1];
                    if (Password != null && Username != null)
                    {   
                        Global.Config.Username = Username;
                        foreach (char c in Password.ToCharArray()) {
                            Global.Password.AppendChar(c);
                        }
                        
                        //LoCyanFrpDesktop.Properties.Settings.Default.username = Username;
                        //LoCyanFrpDesktop.Properties.Settings.Default.password = Password;
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
                ProcessUrlParameters(args);
                for (int x = 0; x < args.Count(); x++) {
                    if (x != args.Count() - 1)
                    {
                        Console.Write(args[x]);
                    }
                    else { 
                        Console.WriteLine(args[x]);
                    }
                }
            }
            
        }
        protected override void OnExit(ExitEventArgs e)
        {
            Cef.Shutdown();
            base.OnExit(e);
        }
        private static void CurrentDomain_UnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;
        }
        private void ProcessUrlParameters(string[] args)
        {
            string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string filePath = Path.Combine(documentsPath, "auto_launch.ini");

            string arguments = string.Join(" ", args);

            try
            {
                File.WriteAllText(filePath, arguments);
            }
            catch (Exception ex)
            {
                // 处理写入文件时可能发生的异常
                MessageBox.Show("写入文件时出现错误：" + ex.Message);
            }
        }
    }
}
