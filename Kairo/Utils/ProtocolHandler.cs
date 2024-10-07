using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Kairo.Utils
{
    internal class ProtocolHandler
    {   
        public static void Init()
        {
            RegisterUrlProtocol("Kairo", Assembly.GetExecutingAssembly().Location);
        }
        public static void RegisterUrlProtocol(string protocolName, string applicationPath)
        {
            using (RegistryKey key = Registry.ClassesRoot.CreateSubKey(protocolName))
            {
                if (key != null)
                {
                    key.SetValue(string.Empty, $"URL:{protocolName} Protocol");
                    key.SetValue("URL Protocol", string.Empty);

                    using (RegistryKey shellKey = key.CreateSubKey(@"shell\open\command"))
                    {
                        if (shellKey != null)
                        {
                            shellKey.SetValue(string.Empty, $"\"{applicationPath}\" \"%1\"");
                        }
                    }
                }
            }
        }
        public static void ProcessUrlParameters(string url)
        {

        }
    }
}
