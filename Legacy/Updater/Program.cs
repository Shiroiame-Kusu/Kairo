using System;
using System.Diagnostics;
using System.IO;
using System.Text;

Stopwatch stopWatch = new();
stopWatch.Start();
ConsoleColor @default = Console.ForegroundColor;

if (Environment.OSVersion.Platform == PlatformID.Win32NT)
{
    Console.Title = "Updater";
    AppDomain.CurrentDomain.ProcessExit += (_, _) =>
    {
        if (File.Exists("Updater.exe.config"))
        {
            File.Delete("Updater.exe.config");
        }
        Process.Start(new ProcessStartInfo("cmd.exe")
        {
            Arguments = "/k del /q Updater.exe & pause & exit",
            WorkingDirectory = Directory.GetCurrentDirectory(),
            UseShellExecute = false
        });
    };
}
Console.WriteLine("正在等待进程退出......");
try
{
    Process.GetProcessById(int.Parse(args[0])).WaitForExit();
}catch(Exception e)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine(e.ToString);
}
Replace();
stopWatch.Stop();
Console.WriteLine($"\r\n替换更新完毕，用时{stopWatch.ElapsedMilliseconds}ms");
Console.ForegroundColor = @default;
Console.ReadKey();

void Replace()
{
    try
    {
        Console.OutputEncoding = Encoding.UTF8;
        File.Delete(Path.Combine(Directory.GetParent(Directory.GetCurrentDirectory()).FullName,"Kairo.exe"));
        File.Copy(Path.Combine(Directory.GetCurrentDirectory(), "update.temp"), Path.Combine(Directory.GetParent(Directory.GetCurrentDirectory()).FullName, "Kairo.exe"));
        Console.WriteLine($"- {Path.GetFullPath(Path.Combine("update.temp"))} 复制成功");
        Console.ForegroundColor = ConsoleColor.White;
    }
    catch (Exception e)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(e.ToString());
    }
}
