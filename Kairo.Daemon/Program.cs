namespace Kairo.Daemon;

class Program
{
    static async Task<int> Main(string[] args)
    {
        // 处理命令行参数
        bool foreground = args.Contains("--foreground") || args.Contains("-f");

        // 写 PID 文件
        var runtimeDir = Kairo.Core.Daemon.DaemonConstants.GetRuntimeDirectory();
        Directory.CreateDirectory(runtimeDir);
        var pidPath = Kairo.Core.Daemon.DaemonConstants.GetPidFilePath();
        await File.WriteAllTextAsync(pidPath, Environment.ProcessId.ToString());

        Console.WriteLine($"[kairo-daemon] PID={Environment.ProcessId}, socket={Kairo.Core.Daemon.DaemonConstants.GetSocketPath()}");

        using var daemon = new DaemonHost();
        using var cts = new CancellationTokenSource();

        // 优雅退出信号
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("[kairo-daemon] 收到退出信号...");
            cts.Cancel();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            cts.Cancel();
            daemon.Dispose();
            // 清理 PID 文件
            try { File.Delete(pidPath); } catch { }
        };

        try
        {
            await daemon.RunAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // 正常退出
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[kairo-daemon] 致命错误: {ex}");
            return 1;
        }
        finally
        {
            try { File.Delete(pidPath); } catch { }
        }

        Console.WriteLine("[kairo-daemon] 已退出");
        return 0;
    }
}
