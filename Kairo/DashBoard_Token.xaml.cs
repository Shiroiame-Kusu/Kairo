using Kairo.Utils;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Wpf.Ui.Appearance;
using Wpf.Ui.Common;
using Wpf.Ui.Controls;
using static Kairo.Utils.PNAP;

namespace Kairo
{
    /// <summary>
    /// Interaction logic for DashBoard_Token.xaml
    /// </summary>
    public partial class DashBoard_Token : UiWindow
    {

        public static bool isFrpcInstalled;
        private static SolidColorBrush DarkBrush = new SolidColorBrush(Color.FromRgb(32, 32, 32));
        private static SolidColorBrush LightBrush = new SolidColorBrush(Color.FromRgb(250, 250, 250));
        public DashBoard_Token(string Token, int Proxy)
        {
            InitializeComponent();
            if (CheckIfFrpcInstalled())
            {
                RunCmdCommand($" -u {Token} -p ", Proxy);
            }
            

        }
        private static void SortOutputHandler(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                Logger.Output(LogType.Info, e.Data);
            }
        }
        private static void RunCmdCommand(string command, int ProxyID)
        {
            // 创建一个 ProcessStartInfo 对象
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = Global.Config.FrpcPath, // 指定要运行的命令行程序
                Arguments = command + ProxyID, // 使用 /k 参数保持 cmd 窗口打开，显示输出内容
                Verb = "runas",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false, // 设置为 true 以便在新窗口中显示命令行窗口
                CreateNoWindow = true, // 设置为 false 以显示命令行窗口
                StandardOutputEncoding = Encoding.UTF8
            };
            // 启动进程
            Process _FrpcProcess = Process.Start(psi);
            _FrpcProcess.BeginOutputReadLine();

            _FrpcProcess.OutputDataReceived += SortOutputHandler;
            _FrpcProcess.EnableRaisingEvents = true;

            // 读取标准输出和标准错误输出
            //string output = process.StandardOutput.ReadToEnd(); 
            //string error = process.StandardError.ReadToEnd();

            // 等待进程完成
            //process.WaitForExit();

            // 打印输出
            //Console.WriteLine("Output:\n" + output);
            //Console.WriteLine("Error:\n" + error);

            // 可以将输出存储到变量中，以便后续处理
            // string combinedOutput = output + error;
        }
        public bool CheckIfFrpcInstalled()
        {
            if (!string.IsNullOrEmpty(Global.Config.FrpcPath))
            {
                if (!File.Exists(Global.Config.FrpcPath))
                {
                    bool isConfirmed = Logger.MsgBox("您需要我们自动为您安装frpc吗?", "未检测到您安装的frpc", 1, 47, 1);
                    if (isConfirmed)
                    {
                        DownloadFrpc();
                    }
                    else
                    {
                        Environment.Exit(0);
                    }
                    return false;
                }
                else
                {
                    return true;
                }

            }
            else
            {
                bool isConfirmed = Logger.MsgBox("您需要我们自动为您安装frpc吗?", "未检测到您安装的frpc", 1, 47, 1);
                if (isConfirmed)
                {
                    DownloadFrpc();
                }
                else
                {
                    Environment.Exit(0);
                }
                return false;
            }
            return false;
        }
        public void DownloadFrpc()
        {
            Download downloader = new Download();
            downloader.Owner = this;
            downloader.Show();
        }
        public void OpenSnackbar(string title, string message, SymbolRegular icon)
        {
            Dispatcher.Invoke(() =>
            {
                Snackbar.Show(title, message, icon);
            });
        }
        private void UiWindow_Closing(object sender, CancelEventArgs e)
        {
            e.Cancel = true;
            ShowInTaskbar = false;
            Hide();
        }
        public void UiWindow_StateChanged(object sender, EventArgs e)
        {
        }
        public void UiWindow_ContentRendered(object sender, EventArgs e)
        {

        }
        public void UiWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
            => ShowInTaskbar = IsVisible;
        public void Hide_Click(object sender, RoutedEventArgs e)
        {
            ShowInTaskbar = false;
            Hide();
        }

        public void Exit_Click(object sender, RoutedEventArgs e)
        {
            Environment.Exit(0);
        }
        public void UiWindow_Loaded(object sender, EventArgs e)
        {
            ChangeColor();
        }
        public void ChangeColor()
        {
            Resources["ShadowColor"] = Global.isDarkThemeEnabled ? Colors.White : Colors.LightGray;
            SolidColorBrush solidColorBrush = new SolidColorBrush(Global.isDarkThemeEnabled ? Colors.LightGray : Colors.WhiteSmoke);
            Resources["MainBackgroundColor"] = solidColorBrush;
            if (Global.isDarkThemeEnabled)
            {
                this.Background = DarkBrush;
                Theme.Apply(ThemeType.Dark);
            }
            else
            {
                this.Background = LightBrush;
                Theme.Apply(ThemeType.Light);
            }
            //Theme.Apply(Global.isDarkThemeEnabled ? ThemeType.Dark : ThemeType.Light);
            //this.Background = solidColorBrush;
        }
    }
}
