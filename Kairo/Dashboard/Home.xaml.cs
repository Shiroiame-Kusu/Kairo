using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wpf.Ui.Controls;
using Kairo.Utils;
using System.Windows.Threading;
using System.IO;
using Kairo.Extensions;
using System.Security.Cryptography;
using Markdig;
using HtmlAgilityPack;
using System.Linq;
using System.Windows;
using System.Threading.Tasks;
using HandyControl.Tools.Extension;


namespace Kairo.Dashboard
{
    /// <summary>
    /// Interaction logic for ProxyList.xaml
    /// </summary>
    public partial class Home : UiPage
    {
        public static ImageBrush AvatarImage;
        public Home()
        {
            InitializeCustomComponents();
            RefreshAvatar();
            Task.Run(() => {
                
                FetchAnnouncement();
                CheckIsSignedTodayOrNot();
            });
        }

        private async void CheckIsSignedTodayOrNot()
        {
            using (System.Net.Http.HttpClient hc = new())
            {
                hc.DefaultRequestHeaders.Add("Authorization",$"Bearer {Global.Config.AccessToken}");
                var result = await hc.GetAsync($"{Global.APIList.GetSign}{Global.Config.ID}").Await().Content.ReadAsStringAsync();
                if (result == null) return;
                var temp = JObject.Parse(result);
                if (int.Parse(temp["status"].ToString()) == 200)
                {
                    if ((bool)temp["data"]["status"])
                    {
                        Dispatcher.BeginInvoke(() =>
                        {
                            ToSign.Visibility = Visibility.Hidden;
                            SignStatus.Visibility = Visibility.Visible;
                        });
                    }
                }
                else
                {
                    Console.WriteLine(temp["status"].ToString() + temp["message"].ToString());
                }

            }
        }

        private void InitializeCustomComponents()
        {
            InitializeComponent();
            DataContext = this;
            title_username.Text += Global.Config.Username;
            Resources["BorderColor"] = Global.isDarkThemeEnabled ? Colors.White : Colors.LightGray;
            Traffic.Text += $"{(MainWindow.Traffic / 1024)}GB";
            BandWidth.Text += $"{MainWindow.Inbound * 8 / 1024}/{MainWindow.Outbound * 8 / 1024}Mbps";
        }
        
        private async void FetchAnnouncement()
        {
            try
            {
                using (HttpClient client = new())
                {
                    var result = await client.GetAsync(Global.APIList.GetNotice).Await().Content.ReadAsStringAsync();
                    var result2 = JObject.Parse(result);
                    if (result2 != null && int.Parse(result2["status"].ToString()) == 200) {
                        var html = Markdown.ToHtml(result2["data"]["broadcast"].ToString());
                        var htmlDoc = new HtmlDocument();
                        if (Global.isDarkThemeEnabled)
                        {
                            htmlDoc.LoadHtml(html);
                            var cssContent = "* { color: white; } a { color: aqua} html {background: none !important;}";
                            var styleNode = HtmlNode.CreateNode($"<style>{cssContent}</style>");
                            //var scriptNode = HtmlNode.CreateNode("<script src='https://cdn.jsdelivr.net/npm/smooth-scrollbar@8.6.3/dist/smooth-scrollbar.js'></script>");
                            var newHeadNode = htmlDoc.CreateElement("head");
                            newHeadNode.AppendChild(styleNode);
                            //newHeadNode.AppendChild(scriptNode);
                            htmlDoc.DocumentNode.PrependChild(newHeadNode);
                            InitializeWebView(htmlDoc.DocumentNode.OuterHtml);
                        }
                        else
                        {
                            InitializeWebView(html);
                        }
                        

                    }

                }
            }
            catch (Exception _) {
            }
        }
        private async void InitializeWebView(string a)
        {
            //if (!Directory.Exists(Path.Combine(Global.PATH, "WebView2"))) Directory.CreateDirectory(Path.Combine(Global.PATH, "WebView2"));
            //await WebViewInstaller.CheckAndInstallAsync(false, false, Path.Combine(Global.PATH, "WebView2"));
            Dispatcher.BeginInvoke(async () =>
            {
                await webView.EnsureCoreWebView2Async();
                webView.CoreWebView2.NavigateToString(a);
            });
            
        }
        private async void RefreshAvatar()
        {
            try
            {
                using (var client = new System.Net.Http.HttpClient())
                {
                    var Avatar2 = await client.GetAsync(MainWindow.Avatar).Await().Content.ReadAsStreamAsync();
                    var path = System.IO.Path.Combine(Global.PATH, "Avatar.png");
                    var ApplyAvatar = () =>
                    {
                        BitmapImage bitmap = new(new Uri(path));
                        ImageBrush imageBrush = new() { 
                            ImageSource = bitmap,
                            Stretch = Stretch.UniformToFill,
                        };
                        // 设置 BitmapImage 的 UriSource 属性为图片文件的路径
                        Application.Current.Dispatcher.BeginInvoke(() =>
                        {
                            Avatar.Background = Avatar.Background.Clone();
                            Avatar.Background = imageBrush;


                        });
                    };

                    if (File.Exists(path)){ 
                        MD5 md5 = MD5.Create();
                        if (md5.ComputeHash(Avatar2).SequenceEqual(md5.ComputeHash(File.ReadAllBytes("Avatar.png"))))
                        {   
                            ApplyAvatar();
                            return;
                        }

                        File.Delete(path);
                        
                    }
                    using (FileStream fileStream = new(path, FileMode.Create))
                    {
                        byte[] bytes = new byte[Avatar2.Length];
                        Avatar2.Read(bytes, 0, bytes.Length);
                        // 设置当前流的位置为流的开始
                        Avatar2.Seek(0, SeekOrigin.Begin);

                        // 把 byte[] 写入文件

                        BinaryWriter bw = new BinaryWriter(fileStream);
                        bw.Write(bytes);
                        bw.Close();
                        fileStream.Close();
                    }
                    ApplyAvatar();

                }

            }
            catch (Exception ex)
            {
                Logger.Output(LogType.Error, ex);
                Logger.MsgBox("无法获取您的头像, 请稍后重试", "Kairo", 0, 48, 1);
            }
        }

        private void ToSign_Click(object sender, RoutedEventArgs e)
        {
            Task.Run(() => {
                using (var hc = new HttpClient())
                {
                    hc.DefaultRequestHeaders.Add("Authorization", $"Bearer {Global.Config.AccessToken}");
                    HttpResponseMessage responseMessage = hc.PostAsync($"{Global.APIList.GetSign}{Global.Config.ID}", null).Result;
                    
                    
                    var temp = JObject.Parse(responseMessage.Content.ReadAsStringAsync().Result);
                    if (!string.IsNullOrEmpty(temp.ToString()) && int.Parse(temp["status"].ToString()) == 200) {
                        int i = int.Parse(temp["data"]["get_traffic"].ToString());
                        Logger.MsgBox($"签到成功\n您获得 {i}GB 流量", "Kairo", 0, 48, 1);
                        Dispatcher.BeginInvoke(() =>
                        {
                            Traffic.Text = $"剩余流量: {(MainWindow.Traffic / 1024) + i}GB";
                        });
                        Dispatcher.BeginInvoke(() =>
                        {
                            ToSign.Visibility = Visibility.Collapsed;
                            SignStatus.Visibility = Visibility.Visible;
                        });
                    }
                    if(int.Parse(temp["status"].ToString()) == 403)
                    {
                        if (temp["message"].ToString() == "你今天已经签到过了")
                        {   

                            Logger.MsgBox("你今天已经签到过了", "Kairo", 0, 48, 1);
                            Dispatcher.BeginInvoke(() =>
                            {
                                ToSign.Visibility = Visibility.Collapsed;
                                SignStatus.Visibility = Visibility.Visible;
                            });
                            return;
                        }
                        Logger.MsgBox("无法连接到服务器, 请检查您的网络链接", "Kairo", 0, 48, 1);
                        return;
                    }
                    if (!responseMessage.IsSuccessStatusCode)
                    {
                        Logger.MsgBox("无法连接到服务器, 请检查您的网络链接", "Kairo", 0, 48, 1);
                        return;
                    }
                }
            });
        }
    }
}

