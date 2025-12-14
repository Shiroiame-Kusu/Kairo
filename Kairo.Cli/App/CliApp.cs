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

        // 使用 OAuth Code 登录（code -> refresh_token -> access_token）
        if (!string.IsNullOrWhiteSpace(options.OAuthCode))
        {
            if (!await PerformLoginWithCodeAsync(options.OAuthCode))
                return 1;
        }

        // 使用 refresh token 登录（已有 refresh_token 时使用）
        if (!string.IsNullOrWhiteSpace(options.RefreshToken))
        {
            if (!await PerformLoginWithRefreshTokenAsync(options.RefreshToken))
                return 1;
        }

        // 检查是否已登录，未登录则进入交互式登录流程
        var frpToken = !string.IsNullOrWhiteSpace(options.FrpToken) 
            ? options.FrpToken 
            : CliConfigManager.Config.FrpToken;

        if (string.IsNullOrWhiteSpace(frpToken))
        {
            if (options.InteractiveMode)
            {
                var loginResult = await InteractiveLoginAsync();
                if (!loginResult)
                    return 1;
                // 重新获取 frpToken
                frpToken = CliConfigManager.Config.FrpToken;
            }
            else
            {
                Console.WriteLine("[警告] 未找到 FRP Token，请先通过 OAuth 登录");
                Console.WriteLine();
                ShowOAuthUrl();
                Console.WriteLine();
                Console.WriteLine("获取授权码后，使用以下命令登录:");
                Console.WriteLine("  kairo-cli --code <your_code>");
                Console.WriteLine();
                return 1;
            }
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
            Console.WriteLine();
        }

        // 获取隧道列表
        List<Tunnel>? tunnels = null;
        
        if (options.ListProxies || options.ProxyIds.Count == 0)
        {
            _apiClient ??= new ApiClient();
            await _apiClient.EnsureLoggedInAsync();
            
            tunnels = await _apiClient.GetTunnelsAsync();
            if (tunnels == null || tunnels.Count == 0)
            {
                Console.WriteLine("[信息] 没有可用的隧道");
                return 0;
            }

            ShowTunnelList(tunnels);
            
            if (options.ListProxies)
            {
                Console.WriteLine("使用以下命令启动隧道:");
                Console.WriteLine("  kairo-cli --proxy <id1,id2,...>");
                return 0;
            }

            if (options.InteractiveMode)
            {
                // 交互式选择隧道
                var selectedIds = await InteractiveSelectTunnelsAsync(tunnels);
                if (selectedIds == null || selectedIds.Count == 0)
                {
                    Console.WriteLine("[错误] 未选择有效的隧道");
                    return 1;
                }
                
                foreach (var id in selectedIds)
                    options.ProxyIds.Add(id);
            }
            else
            {
                // 非交互模式下，未指定隧道则启动全部
                Console.WriteLine("[信息] 未指定隧道 ID，将启动全部隧道");
                foreach (var tunnel in tunnels)
                    options.ProxyIds.Add(tunnel.Id);
            }
        }

        return await StartProxiesAsync(frpcPath, frpToken!, options.ProxyIds);
    }

    /// <summary>
    /// 交互式登录流程
    /// </summary>
    private async Task<bool> InteractiveLoginAsync()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("                     欢迎使用 Kairo CLI");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();
        Console.WriteLine("检测到您尚未登录，需要先完成 OAuth 授权。");
        Console.WriteLine();

        var oauthUrl = $"{ApiEndpoints.OAuthAuthorize}?client_id={AppConstants.APPID}&scopes=User,Node,Tunnel,Sign&mode=code";
        
        Console.WriteLine("请在浏览器中打开以下链接进行授权:");
        Console.WriteLine();
        Console.WriteLine($"  {oauthUrl}");
        Console.WriteLine();
        
        // 尝试自动打开浏览器
        Console.Write("是否自动打开浏览器? [Y/n]: ");
        var autoOpen = Console.ReadLine()?.Trim().ToLowerInvariant();
        
        if (autoOpen != "n" && autoOpen != "no")
        {
            TryOpenBrowser(oauthUrl);
        }
        
        Console.WriteLine();
        Console.WriteLine("授权完成后，请将获取到的授权码粘贴到下方:");
        Console.Write("授权码: ");
        
        var code = Console.ReadLine()?.Trim();
        
        if (string.IsNullOrWhiteSpace(code))
        {
            Console.WriteLine("[错误] 授权码不能为空");
            return false;
        }

        return await PerformLoginWithCodeAsync(code);
    }

    /// <summary>
    /// 使用授权码登录
    /// </summary>
    private async Task<bool> PerformLoginWithCodeAsync(string code)
    {
        Console.WriteLine("[信息] 正在使用 OAuth Code 获取令牌...");
        _apiClient = new ApiClient();
        var loginResult = await _apiClient.ExchangeCodeForRefreshTokenAsync(code);
        
        if (!loginResult.Success)
        {
            Console.WriteLine($"[错误] 登录失败: {loginResult.Message}");
            Console.WriteLine();
            Console.WriteLine("[提示] 请重新获取授权码:");
            ShowOAuthUrl();
            return false;
        }
        
        Console.WriteLine($"[成功] 登录成功! 用户: {loginResult.Username}");
        Console.WriteLine($"[信息] 凭据已保存到配置文件");
        Console.WriteLine();
        return true;
    }

    /// <summary>
    /// 使用 Refresh Token 登录
    /// </summary>
    private async Task<bool> PerformLoginWithRefreshTokenAsync(string refreshToken)
    {
        Console.WriteLine("[信息] 正在使用 Refresh Token 进行登录...");
        _apiClient = new ApiClient();
        var loginResult = await _apiClient.LoginWithRefreshTokenAsync(refreshToken);
        
        if (!loginResult.Success)
        {
            Console.WriteLine($"[错误] 登录失败: {loginResult.Message}");
            Console.WriteLine();
            Console.WriteLine("[提示] Refresh Token 可能已过期，请重新获取授权码:");
            ShowOAuthUrl();
            return false;
        }
        
        Console.WriteLine($"[成功] 登录成功! 用户: {loginResult.Username}");
        Console.WriteLine($"[信息] 凭据已保存到配置文件");
        Console.WriteLine();
        return true;
    }

    /// <summary>
    /// 显示隧道列表
    /// </summary>
    private void ShowTunnelList(List<Tunnel> tunnels)
    {
        Console.WriteLine();
        Console.WriteLine("可用隧道列表:");
        Console.WriteLine("─────────────────────────────────────────────────────────────────────");
        Console.WriteLine($"{"序号",-6} {"ID",-8} {"名称",-20} {"类型",-10} {"本地地址",-20} {"节点",-15}");
        Console.WriteLine("─────────────────────────────────────────────────────────────────────");
        
        for (int i = 0; i < tunnels.Count; i++)
        {
            var tunnel = tunnels[i];
            var localAddr = $"{tunnel.LocalIp}:{tunnel.LocalPort}";
            var nodeName = tunnel.NodeInfo?.Name ?? "未知";
            Console.WriteLine($"{i + 1,-6} {tunnel.Id,-8} {tunnel.ProxyName,-20} {tunnel.ProxyType,-10} {localAddr,-20} {nodeName,-15}");
        }
        
        Console.WriteLine("─────────────────────────────────────────────────────────────────────");
        Console.WriteLine();
    }

    /// <summary>
    /// 交互式选择隧道
    /// </summary>
    private Task<List<int>?> InteractiveSelectTunnelsAsync(List<Tunnel> tunnels)
    {
        Console.WriteLine("请选择要启动的隧道:");
        Console.WriteLine("  - 输入序号 (例如: 1,2,3) 或隧道 ID");
        Console.WriteLine("  - 输入 'all' 或直接按回车启动全部隧道");
        Console.WriteLine("  - 输入 'q' 退出");
        Console.WriteLine();
        Console.Write("您的选择: ");
        
        var input = Console.ReadLine()?.Trim().ToLowerInvariant();
        
        if (input == "q" || input == "quit" || input == "exit")
        {
            Console.WriteLine("[信息] 已取消");
            return Task.FromResult<List<int>?>(null);
        }

        var selectedIds = new List<int>();
        
        if (string.IsNullOrEmpty(input) || input == "all" || input == "a")
        {
            // 启动全部
            foreach (var tunnel in tunnels)
                selectedIds.Add(tunnel.Id);
            Console.WriteLine($"[信息] 已选择全部 {selectedIds.Count} 个隧道");
        }
        else
        {
            // 解析输入
            foreach (var part in input.Split(',', ' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(part.Trim(), out var num))
                {
                    // 判断是序号还是 ID
                    if (num >= 1 && num <= tunnels.Count)
                    {
                        // 可能是序号，检查是否也是有效的 ID
                        var tunnelById = tunnels.FirstOrDefault(t => t.Id == num);
                        var tunnelByIndex = tunnels[num - 1];
                        
                        if (tunnelById != null && tunnelById.Id != tunnelByIndex.Id)
                        {
                            // 既是序号也是 ID，优先使用序号
                            if (!selectedIds.Contains(tunnelByIndex.Id))
                                selectedIds.Add(tunnelByIndex.Id);
                        }
                        else if (tunnelById != null)
                        {
                            // 是 ID
                            if (!selectedIds.Contains(num))
                                selectedIds.Add(num);
                        }
                        else
                        {
                            // 是序号
                            if (!selectedIds.Contains(tunnelByIndex.Id))
                                selectedIds.Add(tunnelByIndex.Id);
                        }
                    }
                    else
                    {
                        // 只能是 ID
                        var tunnel = tunnels.FirstOrDefault(t => t.Id == num);
                        if (tunnel != null && !selectedIds.Contains(num))
                            selectedIds.Add(num);
                        else if (tunnel == null)
                            Console.WriteLine($"[警告] 未找到隧道 ID: {num}");
                    }
                }
            }
        }

        return Task.FromResult<List<int>?>(selectedIds);
    }

    /// <summary>
    /// 尝试打开浏览器
    /// </summary>
    private void TryOpenBrowser(string url)
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                Process.Start(new ProcessStartInfo("xdg-open", url) { UseShellExecute = false });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start(new ProcessStartInfo("open", url) { UseShellExecute = false });
            }
            else if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo("cmd", $"/c start {url.Replace("&", "^&")}") { UseShellExecute = false });
            }
            Console.WriteLine("[信息] 已尝试打开浏览器");
        }
        catch
        {
            Console.WriteLine("[提示] 无法自动打开浏览器，请手动复制链接");
        }
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
        bool noInteractive = false;

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
                case "--code":
                    if (i + 1 < _args.Length) options.OAuthCode = _args[++i];
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
                case "--no-interactive":
                    noInteractive = true;
                    break;
            }
        }

        // 如果没有禁用交互模式且标准输入可用，则启用交互模式
        if (!noInteractive && !Console.IsInputRedirected)
        {
            options.InteractiveMode = true;
        }

        return options;
    }

    private void ShowHelp()
    {
        Console.WriteLine("用法: kairo-cli [选项]");
        Console.WriteLine();
        Console.WriteLine("选项:");
        Console.WriteLine("  --oauth, --get-oauth-url  显示 OAuth 授权 URL");
        Console.WriteLine("  --code <code>             使用 OAuth 授权码登录");
        Console.WriteLine("  --refresh-token, -r <token>");
        Console.WriteLine("                            使用 Refresh Token 登录（高级）");
        Console.WriteLine("  --frp-token, -t <token>   指定 FRP Token");
        Console.WriteLine("  --frpc-path, -f <path>    指定 frpc 可执行文件路径");
        Console.WriteLine("  --proxy, -p <id1,id2,...> 指定要启动的隧道 ID");
        Console.WriteLine("  --list, -l                列出所有可用隧道");
        Console.WriteLine("  --no-interactive          禁用交互模式");
        Console.WriteLine("  --version, -v             显示版本信息");
        Console.WriteLine("  --help, -h                显示此帮助信息");
        Console.WriteLine();
        Console.WriteLine("使用说明:");
        Console.WriteLine("  直接运行 'kairo-cli' 将进入交互式向导模式:");
        Console.WriteLine("    - 未登录时会自动引导完成 OAuth 授权");
        Console.WriteLine("    - 登录后会显示隧道列表并让您选择要启动的隧道");
        Console.WriteLine();
        Console.WriteLine("  也可以使用命令行参数完成各步骤:");
        Console.WriteLine("    kairo-cli --oauth          # 仅显示授权 URL");
        Console.WriteLine("    kairo-cli --code <code>    # 使用授权码登录");
        Console.WriteLine("    kairo-cli --list           # 列出所有隧道");
        Console.WriteLine("    kairo-cli --proxy 1,2,3    # 启动指定隧道");
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
        var oauthUrl = $"{ApiEndpoints.OAuthAuthorize}?client_id={AppConstants.APPID}&scopes=User,Node,Tunnel,Sign&mode=code";
        
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("请在浏览器中打开以下 URL 进行授权:");
        Console.WriteLine();
        Console.WriteLine(oauthUrl);
        Console.WriteLine();
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();
        Console.WriteLine("授权完成后，您将获得一个授权码 (Code)。");
        Console.WriteLine("请复制该授权码，然后使用以下命令完成登录:");
        Console.WriteLine();
        Console.WriteLine("  kairo-cli --code <your_code>");
    }

    private class CliOptions
    {
        public bool ShowHelp { get; set; }
        public bool ShowVersion { get; set; }
        public bool GetOAuthUrl { get; set; }
        public bool ListProxies { get; set; }
        public bool InteractiveMode { get; set; }
        public string? OAuthCode { get; set; }
        public string? RefreshToken { get; set; }
        public string? FrpToken { get; set; }
        public string? FrpcPath { get; set; }
        public List<int> ProxyIds { get; } = new();
    }
}
