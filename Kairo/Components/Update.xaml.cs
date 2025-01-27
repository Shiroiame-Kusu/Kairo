using Kairo.Utils;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using Wpf.Ui.Controls;

namespace Kairo.Components
{
    /// <summary>
    /// Interaction logic for Window1.xaml
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
                IncomingVersion.Text = $"Ver{updateInfo.Version}-{updateInfo.Channel}.{updateInfo.Subversion}";
                foreach(string i in updateInfo.UpdatedWhat)
                {
                    UpdateInfos.Items.Add(i);
                }
                
            });

        }
        private void Confirm_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            Utils.Update.DownloadUpdate(updateInfos);
        }

        private void Cancel_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            Access.Update.Close();
        }
    }
}
