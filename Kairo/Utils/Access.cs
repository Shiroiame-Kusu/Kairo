using Kairo.Dashboard;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Kairo.Utils
{
    internal static class Access
    {
        public static Status? Status { get; set; }
        public static MainWindow? MainWindow { get; set; }
        public static DashBoard? DashBoard { get; set; }
        public static Settings? Settings { get; set; }
        public static Download? Download { get; set; }
        public static ProxyList? ProxyList { get; set; }
        public static Kairo.Components.Update? Update { get; set; }
        public static Kairo.Utils.Update? Updater { get; set; }
    }
}
