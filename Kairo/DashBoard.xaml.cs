using System;
using System.Collections.Generic;
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
//using System.Windows.Shapes;
using System.Net.Http;
using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.Windows.Forms;
using System.Diagnostics;
using Wpf.Ui.Controls;
using Kairo;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.IO;
using MessageBox = Wpf.Ui.Controls.MessageBox;
using Kairo.Utils;
using Wpf.Ui.Common;
using Microsoft.VisualBasic;
using System.Threading;
using System.Windows.Shapes;
using Wpf.Ui.Appearance;

namespace Kairo
{
    /// <summary>
    /// DashBoard.xaml 的交互逻辑
    /// </summary>
    public partial class DashBoard : UiWindow
    {
        public static bool isFrpcInstalled;
        private static SolidColorBrush DarkBrush = new SolidColorBrush(Color.FromRgb(32, 32, 32));
        private static SolidColorBrush LightBrush = new SolidColorBrush(Color.FromRgb(250, 250, 250));
        /// <summary>
        /// 
        /// </summary>
        public DashBoard()
        {
            InitializeComponent();
            Access.DashBoard = this;
            
            
            
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
            else {
                this.Background = LightBrush;
                Theme.Apply(ThemeType.Light);
            }
            //Theme.Apply(Global.isDarkThemeEnabled ? ThemeType.Dark : ThemeType.Light);
            //this.Background = solidColorBrush;
        }
        
    }
}
