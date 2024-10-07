using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using Wpf.Ui.Controls;
using LoCyanFrpDesktop.Utils;
using LoCyanFrpDesktop.Dashboard;
using System.Windows.Threading;
using System.Text.RegularExpressions;
using System.IO;
using LoCyanFrpDesktop.Extensions;
using System.Security.Cryptography;
using Markdig;
using System.Windows.Controls;
using CefSharp;
using CefSharp.Wpf;
using System.Text;
using HtmlAgilityPack;


namespace LoCyanFrpDesktop.Dashboard
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
            //Wait For Rewrite.
            //InitializeAutoLaunch();
            RefreshAvatar();
            FetchAnnouncement();
        }
        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
        }

        private void InitializeCustomComponents()
        {
            Cef.Initialize(new CefSettings()
            {
                //By default CefSharp will use an in-memory cache, you need to specify a Cache Folder to persist data
                CachePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CefSharp\\Cache"),
                LogSeverity = LogSeverity.Verbose,
                LogFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs\\CEF.log")
            });
            InitializeComponent();
            DataContext = this;
            title_username.Text += Global.Config.Username;
            Resources["BorderColor"] = Global.isDarkThemeEnabled ? Colors.White : Colors.LightGray;
            Traffic.Text += $"{(MainWindow.Traffic / 1024)}GB";
            BandWidth.Text += $"{MainWindow.Inbound}/{MainWindow.Outbound}Mbps";
        }
        private async void FetchAnnouncement()
        {
            try
            {
                using (HttpClient client = new())
                {
                    client.BaseAddress = new Uri("https://api.locyanfrp.cn/App/GetBroadCast");
                    var result = await client.GetAsync(client.BaseAddress).Await().Content.ReadAsStringAsync();
                    var result2 = JObject.Parse(result);
                    if (result2 != null && (bool)result2["status"]) {
                        var html = Markdown.ToHtml(result2["broadcast"].ToString());
                        var htmlDoc = new HtmlDocument();
                        if (Global.isDarkThemeEnabled)
                        {
                            htmlDoc.LoadHtml(html);
                            var cssContent = "* { color: white; } a { color: aqua}";
                            var styleNode = HtmlNode.CreateNode($"<style>{cssContent}</style>");
                            var scriptNode = HtmlNode.CreateNode("<script src='https://cdn.jsdelivr.net/npm/smooth-scrollbar@8.6.3/dist/smooth-scrollbar.js'></script>");
                            var newHeadNode = htmlDoc.CreateElement("head");
                            newHeadNode.AppendChild(styleNode);
                            newHeadNode.AppendChild(scriptNode);
                            htmlDoc.DocumentNode.PrependChild(newHeadNode);
                        }
                        Browser.LoadHtml(Global.isDarkThemeEnabled ? htmlDoc.DocumentNode.OuterHtml: html, "http://localhost",Encoding.UTF8);
                        Browser.LoadingStateChanged += OnLoadingStateChanged;
                        
                    }

                }
            }
            catch (Exception _) {
            
            }
        }
        private void OnLoadingStateChanged(object sender, LoadingStateChangedEventArgs e)
        {
            if (!e.IsLoading)
            {
                string css = @"
                html {
                    scroll-behavior: smooth;
                }
                ::-webkit-scrollbar {
                    width: 8px;
                    opacity: 0;
                    transition: opacity 0.5s;
                }
                ::-webkit-scrollbar-track {
                    background: #555;
                    border-radius: 10px; /* Rounded corners for the track */
                }
                ::-webkit-scrollbar-thumb {
                    background: #f1f1f1;
                    
                     border-radius: 10px; /* Rounded corners for the track */
                }
                ::-webkit-scrollbar-thumb:hover {
                    background: #888;
                    
                }
                .show-scrollbar ::-webkit-scrollbar {
                opacity: 1;";
                string script = $"var style = document.createElement('style'); style.innerHTML = `{css}`; document.head.appendChild(style);";
                string script2 = "let timeout; document.addEventListener('scroll', function() { document.documentElement.classList.add('show-scrollbar'); clearTimeout(timeout); timeout = setTimeout(() => { document.documentElement.classList.remove('show-scrollbar'); }, 1000); });";
                Browser.ExecuteScriptAsync(script);
                Browser.ExecuteScriptAsync(script2);

            }
        }
        private async void RefreshAvatar()
        {
            try
            {
                using (var client = new HttpClient())
                {
                    
                    client.BaseAddress = new Uri(MainWindow.Avatar);
                    var Avatar = await client.GetAsync(client.BaseAddress).Await().Content.ReadAsStreamAsync();
                    var path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Avatar.png");
                    var ApplyAvatar = () =>
                    {
                        BitmapImage bitmap = new BitmapImage();

                        // 设置 BitmapImage 的 UriSource 属性为图片文件的路径
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(path, UriKind.RelativeOrAbsolute);
                        bitmap.EndInit();
                        AvatarImage = new ImageBrush(bitmap)
                        {
                            Stretch = Stretch.UniformToFill,

                        };

                        Dispatcher.Invoke(() =>
                        {
                            this.Avatar.Background = AvatarImage;
                        });
                    };

                    if (File.Exists(path)){ 
                        MD5 md5 = MD5.Create();
                        if (md5.ComputeHash(Avatar).Equals(md5.ComputeHash(File.ReadAllBytes("Avatar.png"))))
                        {
                            ApplyAvatar();
                            return;
                        }

                        File.Delete(path);
                        using (FileStream fileStream = new(path, FileMode.Create))
                        {
                            byte[] bytes = new byte[Avatar.Length];
                            Avatar.Read(bytes, 0, bytes.Length);
                            // 设置当前流的位置为流的开始
                            Avatar.Seek(0, SeekOrigin.Begin);

                            // 把 byte[] 写入文件

                            BinaryWriter bw = new BinaryWriter(fileStream);
                            bw.Write(bytes);
                            bw.Close();
                            fileStream.Close();
                            fileStream.Dispose();
                        }
                        ApplyAvatar();
                    }


                }

            }
            catch (Exception ex)
            {
                Logger.MsgBox("无法获取您的头像, 请稍后重试", "LocyanFrp", 0, 48, 1);
            }
        }


    }
}


//BreakAutoCompileBecauseTheRewriteIsNOTFinished
