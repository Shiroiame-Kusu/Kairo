using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Kairo.Components.OAuth
{
    class OAuthCallbackHandler
    {
        public OAuthCallbackHandler() { 
            
        }
        public static void Init()
        {   

            var process = UsePortWithProcess(16092);
            if(process != null)
            {
                KillProcessById(process.Id);
            }
            Task.Run(() => {
                string[] a = { "--urls=http://localhost:16092" };
                WebApplicationBuilder builder = WebApplication.CreateBuilder(a);
                builder.Services.AddControllers();
                WebApplication app = builder.Build();
                app.UseRouting();
                app.MapControllers();
                app.RunAsync();
            });
        }
        public static string ExecCMD(string command)
        {
            System.Diagnostics.Process pro = new System.Diagnostics.Process();
            pro.StartInfo.FileName = "cmd.exe";
            pro.StartInfo.UseShellExecute = false;
            pro.StartInfo.RedirectStandardError = true;
            pro.StartInfo.RedirectStandardInput = true;
            pro.StartInfo.RedirectStandardOutput = true;
            pro.StartInfo.CreateNoWindow = true;
            //pro.StartInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
            pro.Start();
            pro.StandardInput.WriteLine(command);
            pro.StandardInput.WriteLine("exit");
            pro.StandardInput.AutoFlush = true;
            //获取cmd窗口的输出信息
            string output = pro.StandardOutput.ReadToEnd();
            pro.WaitForExit();//等待程序执行完退出进程
            pro.Close();
            return output;

        }

        public static System.Diagnostics.Process UsePortWithProcess(int port)
        {
            try
            {
                ///执行多事获取端口信息
                string cmd_response = ExecCMD("netstat -ano");
                byte[] txt = System.Text.UTF8Encoding.UTF8.GetBytes(cmd_response.ToCharArray());
                using (Stream readStream = new MemoryStream(txt))
                {
                    readStream.Position = 0;
                    using (StreamReader reader = new StreamReader(readStream))
                    {
                        ///正则表达式 用于提取信息
                        Regex reg = new Regex(" \\s+ ", RegexOptions.Compiled);
                        string line = null;
                        while ((line = reader.ReadLine()) != null)
                        {
                            line = line.Trim();
                            ///提取需要的端口相关信息行
                            if (line.StartsWith("TCP", StringComparison.OrdinalIgnoreCase))
                            {
                                line = reg.Replace(line, ",");
                                string[] arr = line.Split(',');
                                if (arr[1].EndsWith($":{port}"))
                                {
                                    int pid = Int32.Parse(arr[4]);
                                    Process p = Process.GetProcessById(pid);
                                    return p;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { }
            return null;
        }

        static void KillProcessById(int processId)
        {
            try
            {
                var process = Process.GetProcessById(processId);
                Console.WriteLine($"正在终止进程: {process.ProcessName} (PID: {processId})");
                process.Kill();
                process.WaitForExit(3000); // 等待3秒
            }
            catch (ArgumentException)
            {
                throw new Exception($"进程 ID {processId} 不存在");
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                throw new Exception($"权限不足，无法终止进程 {processId}: {ex.Message}");
            }
        }
    }
}
