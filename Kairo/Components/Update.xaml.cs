using Downloader;
using Kairo.Utils;
using System.ComponentModel;
using System;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Threading;
using Wpf.Ui.Controls;
using System.Threading.Tasks;

namespace Kairo.Components
{
    /// <summary>
    /// Interaction logic for Update.xaml
    /// </summary>
    public partial class Update : UiWindow
    {
        private UpdateInfo updateInfos;
        public Update()
        {
            Access.Update = this;
            InitializeComponent();
        }
        public void RefreshData(UpdateInfo updateInfo)
        {
            updateInfos = updateInfo;
            Dispatcher.Invoke(() =>
            {
                IncomingVersion.Text = $"Ver {updateInfo.Version} \"{updateInfo.VersionCode}\" {updateInfo.Channel} {updateInfo.Subversion}";
                foreach(string i in updateInfo.UpdatedWhat)
                {
                    UpdateInfos.Items.Add(i);
                }
                
            });

        }

        

        private void Confirm_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            Utils.Update.DownloadUpdate(updateInfos);
            Cancel.IsEnabled = false;
            //Access.Update.Close();
        }

        private void Cancel_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            Access.Update.Close();
        }
    }
}
