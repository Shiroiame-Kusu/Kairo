using Kairo.Utils;
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
using System.Windows.Navigation;
using System.Windows.Shapes;
using Wpf.Ui.Controls;
using Kairo.Dashboard;
using System.Windows.Forms;
using static System.Windows.Forms.LinkLabel;
using System.Windows.Threading;
using System.Diagnostics;
using System.Threading;

namespace Kairo.Dashboard
{
    /// <summary>
    /// Interaction logic for Status.xaml
    /// </summary>
    public partial class Status : UiPage
    {
        public static List<string> ListViewList = new List<string>();
        public Status()
        {
            InitializeComponent();
            Access.Status = this;
            lock (LogPreProcess.Process.Cache)
            {
                LogPreProcess.Process.Cache.ForEach(
                    (line) => Dispatcher.Invoke(() => Append(LogPreProcess.Color(line)))
                );
            }
        }
        public void Append(Paragraph paragraph)
        {
            
            Dispatcher.Invoke(() =>
            {
                LogOutput.Document = LogOutput.Document ?? new();
                LogOutput.Document.Blocks.Add(paragraph);
                while (LogOutput.Document.Blocks.Count > 200)
                {
                    LogOutput.Document.Blocks.Remove(LogOutput.Document.Blocks.FirstBlock);
                }
                LogOutput.ScrollToEnd();
            }, System.Windows.Threading.DispatcherPriority.Background);

        }


        private void StopAllProxies_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int i = ProxyList.PNAPList.Count();
                for (int j = 0;j < i; j++)
                {
                    Process.GetProcessById(ProxyList.PNAPList[j].Pid).Kill();
                    ProxyList.PNAPList[j].IsRunning = false;
                    //Refresh();
                }
            }catch(Exception ex) { 
                Console.WriteLine(ex);
                Process KillProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = "taskkill",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    Verb = "runas",
                    Arguments = " /f /im frpc.exe",
                    RedirectStandardOutput = true,
                    StandardOutputEncoding = Encoding.UTF8
                });
                KillProcess.BeginOutputReadLine();
                KillProcess.OutputDataReceived += KillProcess_OutputDataReceived;
                try
                {
                    int i = ProxyList.PNAPList.Count();
                    for (int j = 0; j < i; j++)
                    {
                        ProxyList.PNAPList[j].IsRunning = false;
                    }
                    //Refresh();
                }
                catch { }
                for (int j = 0; j < ProxyList.Cards.Count; j++) {
                    ProxyList.Cards[j].IndicatorLight.Stroke = Brushes.Gray;
                }
            }
        }
        
        private void KillProcess_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                //lineFiltered = LogPreProcess.Filter(e.Data);
                //Console.WriteLine(e.Data);
                Append(LogPreProcess.Color(LogType.Warn,e.Data));
            }
        }

        private void RefreshProxiesList_Click(object sender, RoutedEventArgs e)
        {
            //Refresh();
        }

        private void ProxiesStarted_MouseDown(object sender, MouseButtonEventArgs e)
        {

        }

        private void ProxiesStarted_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {

        }
    }
}
