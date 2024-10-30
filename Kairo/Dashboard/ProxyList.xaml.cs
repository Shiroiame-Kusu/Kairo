using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
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
using Wpf.Ui.Controls;
using Kairo.Utils;
using Kairo.Dashboard;
using System.Windows.Threading;
using System.Text.RegularExpressions;
using System.IO;
using static Kairo.Utils.PNAP;
using MenuItem = Wpf.Ui.Controls.MenuItem;
using ContextMenu = System.Windows.Controls.ContextMenu;
using System.Windows.Media.Animation;
using System.Drawing;
using System.Windows.Shapes;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Kairo.Utils.Components;
using Kairo.Extensions;
using Microsoft.Win32.SafeHandles;
using Newtonsoft.Json.Serialization;
using Kairo.Components;
using System.Security.Policy;
using System.Windows.Forms;
using CefSharp.Wpf;
using Application = System.Windows.Application;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;


namespace Kairo.Dashboard
{
    /// <summary>
    /// Interaction logic for ProxyList.xaml
    /// </summary>
    public partial class ProxyList : UiPage
    {
        public static ObservableCollection<string> Proxies = new ObservableCollection<string>();

        public static List<Proxy> Proxieslist = new List<Proxy>();

        //public PNAPListComp ListComponents = new PNAPListComp();
        public static List<PNAPListComp> PNAPList = new List<PNAPListComp>();
        public static string SelectedProxy { get; set; }
        public static string lineFiltered;
        public static object BackgroundColor;
        public static ContextMenu BackgroundMenu;
        public static List<ProxyCard> Cards = new();
        public ProxyList()
        {
            InitializeComponent();
            Access.ProxyList = this;
            InitializeProxiesAsync();
            //Wait For Rewrite.
            //InitializeAutoLaunch();
            DataContext = this;
            Resources["BorderColor"] = Global.isDarkThemeEnabled ? Colors.White : Colors.LightGray;
            //BackgroundColor = Resources["ControlFillColorDefaultBrush"];
            BackgroundMenu = new();
            //Inbound.Text += MainWindow.Inbound;
            //OutBound.Text += MainWindow.Outbound;
            if(Home.AvatarImage != null)
            {
                Dispatcher.Invoke(() =>
                {
                    this.Avatar.Background = Home.AvatarImage;
                });
            }
        }

        private static async void InitializeProxiesAsync()
        {
            await GetProxiesListAsync();
            // 若返回 null，则表示无隧道或请求失败
            if (Proxies == null)
            {
                Proxies = new ObservableCollection<string>();
            }
            /*
            // 使用 await 关键字确保数据加载完成后再更新 DataContext
            await Dispatcher.InvokeAsync(() =>
            {
                proxies_list.DataContext = this;
            });
            proxies_list.Items.Refresh();*/
        }

        // 封装隧道获取，采用异步请求防止主线程卡死
        private static async Task<ObservableCollection<string>> GetProxiesListAsync()
        {
            // 获取用户名和Token
            string username = Global.Config.Username;
            string token = Global.Config.Token;
            // 实例化序列
            GetProxiesResponseObject responseObject;
            // 创建新的 HttpClient 实例
            using (var client = new HttpClient())
            {
                // 定义API链接
                string url = $"{Global.API}/api/v2/proxy/all?username={username}";
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
                // 防止API报错
                try
                {
                    // 等待请求
                    HttpResponseMessage response = await client.GetAsync(url);
                    string jsonString = await response.Content.ReadAsStringAsync();
                    // 确保请求完成
                    var temp = JObject.Parse(jsonString);
                    // 结果序列化
                    responseObject = JsonConvert.DeserializeObject<GetProxiesResponseObject>(temp["data"].ToString());
                    responseObject.Status = int.Parse(temp["status"].ToString());
                    
                    // 结果转换为字符串
                    
                   
                }
                catch (Exception ex)
                {
                    CrashInterception.ShowException(ex);
                    return null;

                }
            }

            if (responseObject.Status != 200)
            {   
                if(responseObject.Status == 404)
                {
                    return null;
                }
                Logger.MsgBox("获取隧道失败，请重启软件重新登陆账号", "Kairo", 0, 48, 1);
                return null;
            }

            // 初始化列表 proxiesListInName
            List<string> proxiesListInName = new List<string>();
            for (int i = 0; i < responseObject.Proxies.Count; i++)
            {
                Console.WriteLine(i);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Access.ProxyList.ListPanel.Children.Add(new ProxyCard(responseObject.Proxies[i], i));

                });

            }
            Proxieslist = responseObject.Proxies;

            var proxies = new ObservableCollection<string>(proxiesListInName);
            return proxies;
        }

        private static async void RefreshProxyListAsync()
        {       
            try{
                Proxieslist.Clear();
                PNAPList.Clear();
            }
            catch
            {

            }
            Application.Current.Dispatcher.BeginInvoke(() => {
                Access.ProxyList.ListPanel.Children.Clear();
            });
            InitializeProxiesAsync();
        }

        private static int ProxyStarter(string SelectedProxy,int SelectedIndex)
        {
            
            string proxy_name = SelectedProxy;
            int proxy_id = 0;
            if (Global.Config.FrpcPath == null)
            {
                Logger.MsgBox("您尚未安装Frpc,请先安装或者手动指定", "Kairo", 0, 48, 1);
                return 0;
            }
            foreach (var item in Proxieslist)
            {
                if (item.ProxyName == proxy_name)
                {
                    proxy_id = item.Id;
                    break;
                }
            }

            if (proxy_id == 0)
            {
                Logger.MsgBox("无法将隧道名解析为隧道ID，请检查自己的隧道配置", "Kairo", 0, 48, 1);
                return 0;
            }
            Access.DashBoard.Navigation.Navigate(2);
            // 运行frp
            try
            {
                if (PNAP.PNAPList.Any(prcs => prcs.ProcessName == proxy_id))
                {
                    Logger.MsgBox("这个隧道已经启动了哦", "Kairo", 0, 48, 1);
                    return 0;
                }

            }
            catch (Exception ex)
            {

                return RunCmdCommand($" -u {Global.Config.FrpToken} -p ", proxy_id, SelectedIndex);
            }
            return RunCmdCommand($" -u {Global.Config.FrpToken} -p ",proxy_id, SelectedIndex);

        }


        /*private void Proxies_List_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 如果需要判断是否有选中项
            if (proxies_list.SelectedItem != null)
            {
                SelectedProxy = Proxieslist[proxies_list.SelectedIndex].ProxyName;
            }
            else
            {
                // 没有选中项的处理逻辑
            }
        }*/

        private static int RunCmdCommand(string command, int ProxyID, int SelectionIndex)
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
            try
            {
                if (!PNAPList.Any(Process => Process.ProcessName == ProxyID))
                {

                    PNAPList.Add(new PNAPListComp() { ProcessName = ProxyID, IsRunning = true, Pid = _FrpcProcess.Id, ListIndex = SelectionIndex });
                }
                else
                {
                    int Index = PNAPList.FindIndex(Process => Process.ProcessName == ProxyID);
                    PNAPList[Index].IsRunning = true;
                    PNAPList[Index].ListIndex = SelectionIndex;
                    PNAPList[Index].Pid = _FrpcProcess.Id;
                }
            }
            catch (Exception ex)
            {
                PNAPList.Add(new PNAPListComp() { ProcessName = ProxyID, IsRunning = true, Pid = _FrpcProcess.Id, ListIndex = SelectionIndex });
            }

            _FrpcProcess.OutputDataReceived += SortOutputHandler;
            _FrpcProcess.EnableRaisingEvents = true;
            _FrpcProcess.Exited += new EventHandler((sender, e) => _FrpcProcess_Exited(sender, e, SelectionIndex));
            
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
            return _FrpcProcess.Id;
        }

        private static void _FrpcProcess_Exited(object? sender, EventArgs e, int index)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                Cards[index].IndicatorLight.Stroke = Brushes.Gray;
            });
        }
        private void NonStaticExitedEventHandler(int index) {
            Dispatcher.Invoke(() =>
            {
                Cards[index].IndicatorLight.Stroke = Brushes.Gray;
            });
        }
        private static void SortOutputHandler(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                Logger.Output(LogType.Info, e.Data);
            }
        }


        private void ListView_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {

        }
        private void ListView_MouseDown(object sender, MouseEventArgs e)
        {

        }

        
        private void ListCardClickHandler(object sender, MouseButtonEventArgs e)
        {
            //new Card.ContextMenu();
        }

        private void Card_MouseDown(object sender, MouseButtonEventArgs e)
        {
            
        }
        public class ProxyCard : Border
        {
            public int IndexID { get; set; }
            public Ellipse IndicatorLight = new Ellipse()
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                Height = 8,
                Width = 8,
                Stroke = new SolidColorBrush(Colors.Gray)

            };
            public ProxyCard(Proxy ProxyInfo, int CardIndex)
            {   
                IndexID = CardIndex;
                Cards.Add(this);
                //DefaultStyleKeyProperty.OverrideMetadata(typeof(ProxyCard), new FrameworkPropertyMetadata(typeof(ProxyCard)));
                //this.Style.BasedOn = (Style)Application.Current.Resources["Card"];
                string Name = ProxyInfo.ProxyName;
                //this.Name = Name;
                //this.OverridesDefaultStyle = true;
                //this.Style = ProxyList.card.Style;
                //Theme.Apply(ThemeType.Light);
                this.Background = new SolidColorBrush(Global.isDarkThemeEnabled ? Color.FromRgb(53, 53, 53) : Color.FromRgb(210, 210, 210));
                //this.Background = new SolidColorBrush(!string.IsNullOrEmpty((string)ProxyList.BackgroundColor) ? (Color)ProxyList.BackgroundColor : Colors.Gray);
                //this.Background = new SolidColorBrush((Color)ProxyList.BackgroundColor);
                this.BorderThickness = new Thickness(2);
                this.CornerRadius = new CornerRadius(5);
                this.Padding = new Thickness(10);
                this.Margin = new Thickness(0, 0, 10, 0);
                this.HorizontalAlignment = HorizontalAlignment.Left;
                this.VerticalAlignment = VerticalAlignment.Stretch;
                this.MinHeight = 50;
                this.Width = 200;
                this.BorderBrush = new SolidColorBrush();
                StackPanel stackPanel = new StackPanel()
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Left,
                };
                DockPanel dockPanel = new DockPanel()
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment= HorizontalAlignment.Stretch,
                    MinWidth = 100,
                };
                this.Child = stackPanel;
                //this.AddChild(stackPanel);
                //this.AddChild();
                stackPanel.Children.Add(dockPanel);
                dockPanel.Children.Add(new TextBlock()
                {
                    Text = $"{ProxyInfo.ProxyName}",

                });
                
                dockPanel.Children.Add(IndicatorLight);
                stackPanel.Children.Add(new TextBlock()
                {
                    Text = $"{ProxyInfo.LocalIp}:{ProxyInfo.LocalPort} --> Node{ProxyInfo.Node}:{ProxyInfo.RemotePort}",
                });

                //dockPanel.Children.Add();

                ProxyMenu temp = new ProxyMenu(ProxyInfo.ProxyName, CardIndex,this);
                temp.ID = ProxyInfo.Id;
                this.ContextMenu = temp;
                //dockPanel.Children.Add(temp);
                //this.AddChild(temp);

                this.MouseRightButtonDown += this.OnMouseRightButtonDown;
                this.MouseEnter += this.OnMouseEnter;
                this.MouseLeave += this.OnMouseLeave;
            }
            private void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
            {
                
                
            }
            private void ProxyCard_MouseDoubleClick(object sender, MouseButtonEventArgs e)
            {
                //StartProxy_Click(sender, e);
            }
            private void OnMouseEnter(object sender, EventArgs e)
            {
                Border border = (Border)sender;
                ColorAnimation colorAnimation = new ColorAnimation(Colors.Transparent, Colors.Aqua, TimeSpan.FromMilliseconds(200));
                try
                {
                    border.BorderBrush.BeginAnimation(SolidColorBrush.ColorProperty, colorAnimation);
                }
                catch(Exception ex) { CrashInterception.ShowException(ex); }
                
            }
            private void OnMouseLeave(object sender, EventArgs e)
            {
                Border border = (Border)sender;
                ColorAnimation colorAnimation = new ColorAnimation(Colors.Aqua, Colors.Transparent, TimeSpan.FromMilliseconds(200));
                try
                {
                    border.BorderBrush.BeginAnimation(SolidColorBrush.ColorProperty, colorAnimation);
                }
                catch { }


            }
        }
        public class ProxyMenu : ContextMenu
        {
            public int ID { get; set; }
            public int Pid { get; set; }
            public string ProxyName { get; set; }
            public int IndexID = -1;
            public ProxyCard Card { get; set; }
            public ProxyMenu(string proxyName,int IndexID, ProxyCard card)
            {   
                this.Card = card;
                this.IndexID = IndexID;
                Style roundedContextMenuStyle = new Style();

                // 设置样式的 TargetType
                roundedContextMenuStyle.TargetType = typeof(ContextMenu);
                
                // 定义样式的模板
                ControlTemplate template = new ControlTemplate(typeof(ContextMenu));
                FrameworkElementFactory borderFactory = new FrameworkElementFactory(typeof(Border));
                borderFactory.SetValue(Border.BackgroundProperty, new SolidColorBrush(Global.isDarkThemeEnabled ? Color.FromRgb(53, 53, 53) : Color.FromRgb(210, 210, 210)));
                borderFactory.SetValue(Border.BorderThicknessProperty, new Thickness(1));
                borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
                this.Foreground = new SolidColorBrush(Global.isDarkThemeEnabled ? Colors.White : Colors.Black);
                FrameworkElementFactory stackPanelFactory = new FrameworkElementFactory(typeof(StackPanel));
                stackPanelFactory.SetValue(StackPanel.IsItemsHostProperty, true);

                borderFactory.AppendChild(stackPanelFactory);
                template.VisualTree = borderFactory;

                roundedContextMenuStyle.Setters.Add(new Setter(ContextMenu.TemplateProperty, template));

                // 应用样式
                this.Style = roundedContextMenuStyle;
                MenuItem Refresh = new MenuItem()
                {
                    Header = "刷新",


                };
                MenuItem CreateNewProxy = new MenuItem()
                {
                    Header = "新建隧道"
                };
                MenuItem DeleteProxy = new MenuItem()
                {
                    Header = "删除隧道"
                };
                MenuItem StartProxy = new MenuItem()
                {
                    Header = "启动隧道"
                };
                MenuItem StopProxy = new MenuItem()
                {
                    Header = "停止隧道"
                };
                Refresh.Click += Refresh_Click;
                CreateNewProxy.Click += CreateNewProxy_Click;
                DeleteProxy.Click += DeleteProxy_Click;
                StartProxy.Click += StartProxy_Click;
                StopProxy.Click += StopProxy_Click;
                this.Items.Add(Refresh);
                this.Items.Add(new Separator());
                this.Items.Add(CreateNewProxy);
                this.Items.Add(DeleteProxy);
                this.Items.Add(new Separator());
                this.Items.Add(StartProxy);
                this.Items.Add(StopProxy);
                this.BorderBrush = Brushes.Transparent;
                this.BorderThickness = new Thickness(1);
                ProxyName = proxyName;
                //contextMenu.Margin = new Thickness(5);
                //this.CornerRadius = new CornerRadius(5);

            }
            private void StartProxy_Click(object sender, RoutedEventArgs e)
            {
                if (string.IsNullOrEmpty(Global.Config.FrpcPath))
                {
                    Logger.MsgBox("您尚未安装FRPC!", "Kairo", 0, 48, 1);
                    return;
                }
                try
                {
                    int Index = PNAPList.FindIndex(Process => Process.ListIndex == (IndexID != -1 ? IndexID : throw new Exception()));
                    if (!(bool)PNAPList[Index].IsRunning)
                    {
                        Pid = ProxyStarter(this.ProxyName,IndexID);
                        if (Pid != 0)
                        {
                            Card.IndicatorLight.Stroke = Brushes.LightGreen;
                        }
                    }
                    else
                    {
                        Logger.MsgBox("这个隧道已经启动了哦", "Kairo", 0, 48, 1);
                    }
                }
                catch (Exception ex)
                {
                    Pid = ProxyStarter(this.ProxyName, IndexID);
                    if (Pid != 0)
                    {
                        Card.IndicatorLight.Stroke = Brushes.LightGreen;
                    }
                }
                
            }
            
            private static void Refresh_Click(object sender, RoutedEventArgs e)
            {
                RefreshProxyListAsync();
            }

            private void StopProxy_Click(object sender, RoutedEventArgs e)
            {
                //我在写什么，我不知道我在写些什么w
                
                try
                {
                    int Index = PNAPList.FindIndex(Process => Process.ListIndex == IndexID);
                    if (!(bool)PNAPList[Index].IsRunning)
                    {   

                        Logger.MsgBox("这个隧道并没有启动哦", "Kairo", 0, 48, 1);
                    }
                    else
                    {
                        try
                        {
                            Process.GetProcessById(Pid).Kill();
                        }catch (Exception ex)
                        {
                            CrashInterception.ShowException(ex);
                        }
                        Logger.MsgBox("这个隧道成功关闭了哦", "Kairo", 0, 48, 1);

                        PNAPList[Index].IsRunning = false;
                        Card.IndicatorLight.Stroke = Brushes.Gray;
                    }
                }catch( Exception ex)
                {   
                    CrashInterception.ShowException(ex);
                    Logger.MsgBox("这个隧道并没有启动哦", "Kairo", 0, 48, 1);
                }

            }

            private async void DeleteProxy_Click(object sender, RoutedEventArgs e)
            {
                using (HttpClient httpClient = new HttpClient()) {

                    httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {Global.Config.Token}");
                    var b = httpClient.DeleteAsync($"{Global.API}/api/v2/proxy?username={Global.Config.Username}&proxy_id={this.ID}").Await();
                    var a = b.Content.ReadAsStringAsync().Await();

                    if (a == null )
                    {
                        Logger.MsgBox("请检查您的网络连接", "Kairo", 0, 48, 1);
                        return;
                    }
                    if (!b.IsSuccessStatusCode)
                    {
                        Logger.MsgBox($"{JObject.Parse(a)["status"].ToString()} {JObject.Parse(a)["message"].ToString()}", "Kairo", 0, 48, 1);
                        return;
                    }
                    Refresh_Click(sender, e);
                }
            }
            
            private void CreateNewProxy_Click(object sender, RoutedEventArgs e)
            {
                /*ProxyCreator proxyCreator = new ProxyCreator();
                proxyCreator.Show();
                using (HttpClient httpClient = new HttpClient()) { 
                    //httpClient.DefaultRequestHeaders("A")

                }*/
                /*var browserWindow = new Window
                {
                    Width = 800,
                    Height = 500
                };

                // 创建 ChromiumWebBrowser 实例
                var browser = new ChromiumWebBrowser("https://dashboard.locyanfrp.cn/proxies/add")
                {
                    VerticalAlignment = VerticalAlignment.Stretch,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };

                // 将浏览器控件添加到新窗口
                browserWindow.Content = browser;

                // 显示窗口
                browserWindow.Show();
                Process.Start(new ProcessStartInfo("https://dashboard.locyanfrp.cn/proxies/add")
                {
                    UseShellExecute = true
                });*/
                Process.Start(new ProcessStartInfo("https://dashboard.locyanfrp.cn/proxies/add")
                {
                    UseShellExecute = true
                }); 
            }
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            RefreshProxyListAsync();
        }
        private void CreateNewProxy_Click(object sender,RoutedEventArgs e)
        {   
            Process.Start(new ProcessStartInfo("https://dashboard.locyanfrp.cn/proxies/add")
            {
                UseShellExecute = true
            });
            /*var browserWindow = new UiWindow()
            {
                Width = 800,
                Height = 500
            };

            // 创建 ChromiumWebBrowser 实例
            var browser = new ChromiumWebBrowser("https://dashboard.locyanfrp.cn/proxies/add")
            {
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            // 将浏览器控件添加到新窗口
            browserWindow.Content = browser;

            // 显示窗口
            browserWindow.Show();*/
        }
    }
    public class GetProxiesResponseObject
    {
        public int Status { get; set; }
        [JsonProperty("list")]
        public List<Proxy> Proxies { get; set; }
    }
}


//BreakAutoCompileBecauseTheRewriteIsNOTFinished
