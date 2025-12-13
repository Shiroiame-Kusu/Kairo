using Kairo.Cli.Configuration;

namespace Kairo.Cli;

class Program
{
    public static async Task<int> Main(string[] args)
    {
        // 初始化配置
        CliConfigManager.Init();

        var cliApp = new CliApp(args);
        return await cliApp.RunAsync();
    }
}
