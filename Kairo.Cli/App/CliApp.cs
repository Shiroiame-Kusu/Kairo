using System.Diagnostics;
using Kairo.Core;
using Kairo.Core.Models;
using Kairo.Cli.Configuration;
using Kairo.Cli.Services;

namespace Kairo.Cli;

/// <summary>
/// CLI 主应用程序
/// </summary>
public class CliApp
{
    private readonly string[] _args;
    private readonly CancellationTokenSource _cts = new();
    private ApiClient? _apiClient;

    public CliApp(string[] args)
    {
        _args = args;
    }

    public async Task<int> RunAsync()
    {
        Console.WriteLine();
        Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                     Kairo CLI Mode                            ║");
        Console.WriteLine($"║                     Version {AppConstants.Version,-10}                       ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        var options = ParseArguments();

        if (options.ShowHelp)
        {
            ShowHelp();
            return 0;
        }

        if (options.ShowVersion)
        {
            ShowVersion();
            return 0;
        }

        if (options.GetOAuthUrl)
        {
            ShowOAuthUrl();
            return 0;
        }

        // 使用 refresh token 登录
        if (!string.IsNullOrWhiteSpace(options.RefreshToken))
        {
            Console.WriteLine("[信息] 正在使用提供的 Refresh Token 进行登录...");
            _apiClient = new ApiClient();
            var loginResult = await _apiClient.LoginWithRefreshTokenAsync(options.RefreshToken);
            if (!loginResult.Success)
            {
                Console.WriteLine($"[错误] 登录失败: {loginResult.Message}");
                return 1;
            }
            Console.WriteLine($"[成功] 登录成功! 用户: {loginResult.Username}");
            Console.WriteLine($"[信息] FRP Token 已保存到配置文件");
            Console.WriteLine();
        }

        // 检查是否已登录
        var frpToken = !string.IsNullOrWhiteSpace(options.FrpToken) 
            ? options.FrpToken 
            : CliConfigManager.Config.FrpToken;

        if (string.IsNullOrWhiteSpace(frpToken))
        {
            Console.WriteLine("[警告] 未找到 FRP Token，请先通过 OAuth 登录获取密钥");
            Console.WriteLine();
            ShowOAuthUrl();
            Console.WriteLine();
            Console.WriteLine("获取 Refresh Token 后，使用以下命令登录:");
            Console.WriteLine("  kairo-cli --refresh-token <your_refresh_token>");
            Console.WriteLine();
            return 1;
        }

        // 检查 frpc 路径
        var frpcPath = options.FrpcPath ?? CliConfigManager.Config.FrpcPath;
        if (string.IsNullOrWhiteSpace(frpcPath) || !File.Exists(frpcPath))
        {
            Console.WriteLine("[信息] 未找到 frpc 可执行文件，正在下载...");
            var downloader = new FrpcDownloader();
            var downloadResult = await downloader.DownloadAsync(_cts.Token);
            if (!downloadResult.Success)
            {
                Console.WriteLine($"[错误] 下载 frpc 失败: {downloadResult.Message}");
                return 1;
            }
            frpcPath = downloadResult.FrpcPath!;
            Console.WriteLine($"[成功] frpc 已下载到: {frpcPath}");
        }

        // 列出隧道或启动指定隧道
        if (options.ListProxies || options.ProxyIds.Count == 0)
        {
            _apiClient ??= new ApiClient();
            await _apiClient.EnsureLoggedInAsync();
            
            var tunnels = await _apiClient.GetTunnelsAsync();
            if (tunnels == null || tunnels.Count == 0)
            {
                Console.WriteLine("[信息] 没有可用的隧道");
                return 0;
            }

            Console.WriteLine();
            Console.WriteLine("可用隧道列表:");
            Console.WriteLine("─────────────────────────────────────────────────────────────");
            Console.WriteLine($"{"ID",-8} {"名称",-20} {"类型",-10} {"本地地址",-20} {"节点",-15}");
            Console.WriteLine("─────────────────────────────────────────────────────────────");
            
            foreach (var tunnel in tunnels)
            {
                var localAddr = $"{tunnel.LocalIp}:{tunnel.LocalPort}";
                var nodeName = tunnel.NodeInfo?.Name ?? "未知";
                Console.WriteLine($"{tunnel.Id,-8} {tunnel.ProxyName,-20} {tunnel.ProxyType,-10} {localAddr,-20} {nodeName,-15}");
            }
            
            Console.WriteLine("─────────────────────────────────────────────────────────────");
            Console.WriteLine();
            
            if (options.ListProxies)
            {
                Console.WriteLine("使用以下命令启动隧道:");
                Console.WriteLine("  kairo-cli --proxy <id1,id2,...>");
                return 0;
            }

            // 交互模式
            Console.Write("请输入要启动的隧道 ID (多个用逗号分隔，直接回车启动全部): ");
            var input = Console.ReadLine()?.Trim();
            
            if (string.IsNullOrEmpty(input))
            {
                foreach (var tunnel in tunnels)
                    options.ProxyIds.Add(tunnel.Id);
            }
            else
            {
                foreach (var part in input.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (int.TryParse(part.Trim(), out var id))
                        options.ProxyIds.Add(id);
                }
            }

            if (options.ProxyIds.Count == 0)
            {
                Console.WriteLine("[错误] 未指定有效的隧道 ID");
                return 1;
            }
        }

        return await StartProxiesAsync(frpcPath, frpToken, options.ProxyIds);
    }

    private async Task<int> StartProxiesAsync(string frpcPath, string frpToken, List<int> proxyIds)
    {
        Console.WriteLine();
        Console.WriteLine($"[信息] 正在启动 {proxyIds.Count} 个隧道...");
        Console.WriteLine("[信息] 按 Ctrl+C 停止所有隧道");
        Console.WriteLine();

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Console.WriteLine();
            Console.WriteLine("[信息] 正在停止所有隧道...");
            _cts.Cancel();
        };

        var processes = new List<Process>();

        foreach (var proxyId in proxyIds)
        {
            var process = StartFrpcProcess(frpcPath, frpToken, proxyId);
            if (process != null)
            {
                processes.Add(process);
                Console.WriteLine($"[成功] 隧道 {proxyId} 已启动 (PID: {process.Id})");
            }
            else
            {
                Console.WriteLine($"[错误] 隧道 {proxyId} 启动失败");
            }
        }

        if (processes.Count == 0)
        {
            Console.WriteLine("[错误] 没有成功启动的隧道");
            return 1;
        }

        try
        {
            while (!_cts.IsCancellationRequested)
            {
                bool allExited = processes.All(p => p.HasExited);
                if (allExited)
                {
                    Console.WriteLine("[信息] 所有隧道进程已退出");
                    break;
                }
                await Task.Delay(1000, _cts.Token);
            }
        }
        catch (OperationCanceledException) { }

        foreach (var proc in processes)
        {
            try
            {
                if (!proc.HasExited)
                {
                    proc.Kill(true);
                    proc.WaitForExit(3000);
                    Console.WriteLine($"[信息] 已停止进程 PID: {proc.Id}");
                }
            }
            catch { }
        }

        Console.WriteLine("[信息] 所有隧道已停止");
        return 0;
    }

    private Process? StartFrpcProcess(string frpcPath, string frpToken, int proxyId)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = frpcPath,
                Arguments = $"-u {frpToken} -p {proxyId}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            var process = new Process { StartInfo = psi };
            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    Console.WriteLine($"[隧道 {proxyId}] {e.Data}");
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    Console.WriteLine($"[隧道 {proxyId} 错误] {e.Data}");
            };

            if (!process.Start()) return null;

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            return process;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[错误] 启动 frpc 失败: {ex.Message}");
            return null;
        }
    }

    private CliOptions ParseArguments()
    {
        var options = new CliOptions();

        for (int i = 0; i < _args.Length; i++)
        {
            var arg = _args[i];
            switch (arg.ToLowerInvariant())
            {
                case "--help" or "-h":
                    options.ShowHelp = true;
                    break;
                case "--version" or "-v":
                    options.ShowVersion = true;
                    break;
                case "--oauth" or "--get-oauth-url":
                    options.GetOAuthUrl = true;
                    break;
                case "--refresh-token" or "-r":
                    if (i + 1 < _args.Length) options.RefreshToken = _args[++i];
                    break;
                case "--frp-token" or "-t":
                    if (i + 1 < _args.Length) options.FrpToken = _args[++i];
                    break;
                case "--frpc-path" or "-f":
                    if (i + 1 < _args.Length) options.FrpcPath = _args[++i];
                    break;
                case "--proxy" or "-p":
                    if (i + 1 < _args.Length)
                    {
                        foreach (var part in _args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries))
                        {
                            if (int.TryParse(part.Trim(), out var id))
                                options.ProxyIds.Add(id);
                        }
                    }
                    break;
                case "--list" or "-l":
                    options.ListProxies = true;
                    break;
            }
        }

        return options;
    }

    private void ShowHelp()
    {
        Console.WriteLine("用法: kairo-cli [选项]");
        Console.WriteLine();
        Console.WriteLine("选项:");
        Console.WriteLine("  --oauth, --get-oauth-url  显示 OAuth 授权 URL");
        Console.WriteLine("  --refresh-token, -r <token>");
        Console.WriteLine("                            使用 Refresh Token 登录");
        Console.WriteLine("  --frp-token, -t <token>   指定 FRP Token");
        Console.WriteLine("  --frpc-path, -f <path>    指定 frpc 可执行文件路径");
        Console.WriteLine("  --proxy, -p <id1,id2,...> 指定要启动的隧道 ID");
        Console.WriteLine("  --list, -l                列出所有可用隧道");
        Console.WriteLine("  --version, -v             显示版本信息");
        Console.WriteLine("  --help, -h                显示此帮助信息");
        Console.WriteLine();
        Console.WriteLine("首次使用流程:");
        Console.WriteLine("  1. 运行 'kairo-cli --oauth' 获取授权 URL");
        Console.WriteLine("  2. 在浏览器中打开 URL 并完成授权");
        Console.WriteLine("  3. 复制获取到的 Refresh Token");
        Console.WriteLine("  4. 运行 'kairo-cli --refresh-token <token>' 完成登录");
        Console.WriteLine("  5. 运行 'kairo-cli' 启动隧道");
    }

    private void ShowVersion()
    {
        Console.WriteLine($"Kairo CLI {AppConstants.Version} ({AppConstants.Branch})");
        Console.WriteLine($"Version Name: {AppConstants.VersionName}");
        Console.WriteLine($"Revision: {AppConstants.Revision}");
        Console.WriteLine($"Developer: {AppConstants.Developer}");
        Console.WriteLine(AppConstants.Copyright);
    }

    private void ShowOAuthUrl()
    {
        var oauthUrl = $"{ApiEndpoints.OAuthAuthorize}?client_id={AppConstants.APPID}&scopes=User,Node,Tunnel,Sign&redirect_uri={Uri.EscapeDataString($"{AppConstants.Dashboard}/auth/oauth/redirect-copy")}&mode=copy";
        
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("请在浏览器中打开以下 URL 进行授权:");
        Console.WriteLine();
        Console.WriteLine(oauthUrl);
        Console.WriteLine();
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();
        Console.WriteLine("授权完成后，您将获得一个 Refresh Token。");
        Console.WriteLine("请复制该 Token，然后使用以下命令完成登录:");
        Console.WriteLine();
        Console.WriteLine("  kairo-cli --refresh-token <your_refresh_token>");
    }

    private class CliOptions
    {
        public bool ShowHelp { get; set; }
        public bool ShowVersion { get; set; }
        public bool GetOAuthUrl { get; set; }
        public bool ListProxies { get; set; }
        public string? RefreshToken { get; set; }
        public string? FrpToken { get; set; }
        public string? FrpcPath { get; set; }
        public List<int> ProxyIds { get; } = new();
    }
}
