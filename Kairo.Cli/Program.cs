using Kairo.Cli.Configuration;
using Kairo.Cli.Utils;

namespace Kairo.Cli;

class Program
{
    public static async Task<int> Main(string[] args)
    {
        // 解析日志相关参数（在正式解析前）
        var logLevel = LogLevel.Info;
        var logToFile = false;
        
        foreach (var arg in args)
        {
            switch (arg.ToLowerInvariant())
            {
                case "--debug" or "-d":
                    logLevel = LogLevel.Debug;
                    break;
                case "--log-file":
                    logToFile = true;
                    break;
                case "--quiet" or "-q":
                    logLevel = LogLevel.Warning;
                    break;
            }
        }
        
        // 初始化配置
        CliConfigManager.Init();
        
        // 如果配置中启用了调试模式，覆盖日志级别
        if (CliConfigManager.Config.DebugMode && logLevel > LogLevel.Debug)
            logLevel = LogLevel.Debug;
        if (CliConfigManager.Config.LogToFile)
            logToFile = true;
        
        // 初始化日志系统
        Logger.Initialize(logLevel, logToFile);
        
        Logger.Debug($"命令行参数: {string.Join(" ", args)}");
        Logger.Debug($"配置目录: {Kairo.Core.Configuration.ConfigHelper.GetConfigDirectory()}");
        Logger.Debug($"运行时: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
        Logger.Debug($"操作系统: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
        Logger.Debug($"架构: {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}");

        try
        {
            using var cliApp = new CliApp(args);
            var result = await cliApp.RunAsync();
            Logger.Debug($"程序退出，返回码: {result}");
            return result;
        }
        catch (Exception ex)
        {
            Logger.Exception(ex, "程序运行时发生未处理异常");
            return 1;
        }
    }
}
