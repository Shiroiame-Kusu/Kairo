using System.Diagnostics;
using Kairo.Core;
using Kairo.Core.Models;
using Kairo.Cli.Configuration;
using Kairo.Cli.Services;
using Kairo.Cli.Utils;

namespace Kairo.Cli;

/// <summary>
/// CLI 主应用程序
/// </summary>
public class CliApp : IDisposable
{
    private readonly string[] _args;
    private readonly CancellationTokenSource _cts = new();
    private ApiClient? _apiClient;
    private bool _disposed;
    private bool _cancelKeyRegistered;
    private readonly List<Process> _frpcProcesses = new();

    public CliApp(string[] args)
    {
        _args = args;
        Logger.Debug($"CliApp 实例创建，参数数量: {args.Length}");
        // Ensure frpc child processes are killed on any exit path (SIGTERM, etc.)
        AppDomain.CurrentDomain.ProcessExit += (_, _) => KillAllFrpcProcesses();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Logger.Debug("CliApp Dispose 调用");
        KillAllFrpcProcesses();
        _apiClient?.Dispose();
        _cts.Dispose();
    }

    private void KillAllFrpcProcesses()
    {
        List<Process> snapshot;
        lock (_frpcProcesses)
        {
            snapshot = new List<Process>(_frpcProcesses);
            _frpcProcesses.Clear();
        }
        foreach (var proc in snapshot)
        {
            try
            {
                if (!proc.HasExited)
                {
                    proc.Kill(true);
                    proc.WaitForExit(3000);
                }
            }
            catch { }
            finally
            {
                try { proc.Dispose(); } catch { }
            }
        }
    }

    public async Task<int> RunAsync()
    {
        Logger.MethodEntry();
        Logger.Debug("显示欢迎横幅");
        
        var versionLine = $"Ver {AppConstants.Version} \"{AppConstants.VersionName}\" {AppConstants.Branch.ToDisplayName()} {AppConstants.Revision}";
        var padding = (61 - versionLine.Length) / 2;
        var centeredVersion = versionLine.PadLeft(padding + versionLine.Length).PadRight(61);
        Console.WriteLine();
        Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                       Kairo CLI Mode                          ║");
        Console.WriteLine($"║ {centeredVersion} ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        Logger.Debug("开始解析命令行参数");
        var options = ParseArguments();
        Logger.Debug($"参数解析完成: ShowHelp={options.ShowHelp}, ShowVersion={options.ShowVersion}, GetOAuthUrl={options.GetOAuthUrl}, ListProxies={options.ListProxies}, InteractiveMode={options.InteractiveMode}, ProxyIds.Count={options.ProxyIds.Count}");

        if (options.ShowHelp)
        {
            Logger.Debug("用户请求显示帮助");
            ShowHelp();
            Logger.MethodExit("0 (显示帮助)");
            return 0;
        }

        if (options.ShowVersion)
        {
            Logger.Debug("用户请求显示版本");
            ShowVersion();
            Logger.MethodExit("0 (显示版本)");
            return 0;
        }

        if (options.GetOAuthUrl)
        {
            Logger.Debug("用户请求显示OAuth URL");
            ShowOAuthUrl();
            Logger.MethodExit("0 (显示OAuth URL)");
            return 0;
        }

        // 使用 OAuth Code 登录（code -> refresh_token -> access_token）
        if (!string.IsNullOrWhiteSpace(options.OAuthCode))
        {
            Logger.Info($"使用 OAuth Code 进行登录，Code长度: {options.OAuthCode.Length}");
            if (!await PerformLoginWithCodeAsync(options.OAuthCode))
            {
                Logger.Error("OAuth Code 登录失败");
                Logger.MethodExit("1 (OAuth登录失败)");
                return 1;
            }
            Logger.Info("OAuth Code 登录成功");
        }

        // 使用 refresh token 登录（已有 refresh_token 时使用）
        if (!string.IsNullOrWhiteSpace(options.RefreshToken))
        {
            Logger.Info("使用 Refresh Token 进行登录");
            if (!await PerformLoginWithRefreshTokenAsync(options.RefreshToken))
            {
                Logger.Error("Refresh Token 登录失败");
                Logger.MethodExit("1 (RefreshToken登录失败)");
                return 1;
            }
            Logger.Info("Refresh Token 登录成功");
        }

        // 检查是否已登录，未登录则进入交互式登录流程
        var frpToken = !string.IsNullOrWhiteSpace(options.FrpToken) 
            ? options.FrpToken 
            : CliConfigManager.Config.FrpToken;

        Logger.Debug($"FRP Token 来源: {(!string.IsNullOrWhiteSpace(options.FrpToken) ? "命令行参数" : "配置文件")}");
        Logger.Debug($"FRP Token 是否存在: {!string.IsNullOrWhiteSpace(frpToken)}");

        if (string.IsNullOrWhiteSpace(frpToken))
        {
            Logger.Warning("未找到 FRP Token");
            if (options.InteractiveMode)
            {
                Logger.Info("进入交互式登录流程");
                var loginResult = await InteractiveLoginAsync();
                if (!loginResult)
                {
                    Logger.Error("交互式登录失败");
                    Logger.MethodExit("1 (交互式登录失败)");
                    return 1;
                }
                // 重新获取 frpToken
                frpToken = CliConfigManager.Config.FrpToken;
                Logger.Debug($"交互式登录完成，重新获取FRP Token: {!string.IsNullOrWhiteSpace(frpToken)}");
            }
            else
            {
                Logger.Warning("非交互模式无法登录，需要用户手动授权");
                Console.WriteLine("[警告] 未找到 FRP Token，请先通过 OAuth 登录");
                Console.WriteLine();
                ShowOAuthUrl();
                Console.WriteLine();
                Console.WriteLine("获取授权码后，使用以下命令登录:");
                Console.WriteLine("  kairo-cli --code <your_code>");
                Console.WriteLine();
                Logger.MethodExit("1 (未登录)");
                return 1;
            }
        }

        // 检查 frpc 路径
        var frpcPath = options.FrpcPath ?? CliConfigManager.Config.FrpcPath;
        Logger.Debug($"frpc 路径来源: {(options.FrpcPath != null ? "命令行参数" : "配置文件")}");
        Logger.Debug($"frpc 路径: {frpcPath ?? "(未设置)"}");
        
        if (string.IsNullOrWhiteSpace(frpcPath) || !File.Exists(frpcPath))
        {
            Logger.Info($"frpc 可执行文件不存在，需要下载");
            if (!string.IsNullOrWhiteSpace(frpcPath))
                Logger.Debug($"配置的路径不存在: {frpcPath}");
            Console.WriteLine("[信息] 未找到 frpc 可执行文件，正在下载...");
            using var downloader = new FrpcDownloader { ForceGitHub = options.ForceGitHub };
            if (options.ForceGitHub)
                Logger.Debug("用户强制使用 GitHub 下载源");
            var downloadResult = await downloader.DownloadAsync(_cts.Token);
            if (!downloadResult.Success)
            {
                Logger.Error($"frpc 下载失败: {downloadResult.Message}");
                Console.WriteLine($"[错误] 下载 frpc 失败: {downloadResult.Message}");
                Logger.MethodExit("1 (frpc下载失败)");
                return 1;
            }
            frpcPath = downloadResult.FrpcPath!;
            Logger.Info($"frpc 下载成功: {frpcPath}");
            Console.WriteLine($"[成功] frpc 已下载到: {frpcPath}");
            Console.WriteLine();
        }
        else
        {
            Logger.Debug($"frpc 可执行文件已存在: {frpcPath}");
        }

        // 获取隧道列表
        List<Tunnel>? tunnels = null;
        
        if (options.ListProxies || options.ProxyIds.Count == 0)
        {
            Logger.Debug($"需要获取隧道列表: ListProxies={options.ListProxies}, ProxyIds.Count={options.ProxyIds.Count}");
            _apiClient ??= new ApiClient();
            
            Logger.Debug("确保 API 客户端已登录");
            await _apiClient.EnsureLoggedInAsync();
            
            Logger.Debug("调用 API 获取隧道列表");
            tunnels = await _apiClient.GetTunnelsAsync();
            if (tunnels == null || tunnels.Count == 0)
            {
                Logger.Warning("没有可用的隧道");
                Console.WriteLine("[信息] 没有可用的隧道");
                Logger.MethodExit("0 (无隧道)");
                return 0;
            }
            
            Logger.Info($"获取到 {tunnels.Count} 个隧道");
            ShowTunnelList(tunnels);
            
            if (options.ListProxies)
            {
                Logger.Debug("仅列出隧道，不启动");
                Console.WriteLine("使用以下命令启动隧道:");
                Console.WriteLine("  kairo-cli --proxy <id1,id2,...>");
                Logger.MethodExit("0 (列出隧道)");
                return 0;
            }

            if (options.InteractiveMode)
            {
                Logger.Debug("进入交互式隧道选择");
                // 交互式选择隧道
                var selectedIds = await InteractiveSelectTunnelsAsync(tunnels);
                if (selectedIds == null || selectedIds.Count == 0)
                {
                    Logger.Warning("用户未选择有效的隧道");
                    Console.WriteLine("[错误] 未选择有效的隧道");
                    Logger.MethodExit("1 (未选择隧道)");
                    return 1;
                }
                
                Logger.Info($"用户选择了 {selectedIds.Count} 个隧道: [{string.Join(", ", selectedIds)}]");
                foreach (var id in selectedIds)
                    options.ProxyIds.Add(id);
            }
            else
            {
                // 非交互模式下，未指定隧道则启动全部
                Logger.Info($"非交互模式，将启动全部 {tunnels.Count} 个隧道");
                Console.WriteLine("[信息] 未指定隧道 ID，将启动全部隧道");
                foreach (var tunnel in tunnels)
                    options.ProxyIds.Add(tunnel.Id);
            }
        }
        else
        {
            Logger.Debug($"用户指定了 {options.ProxyIds.Count} 个隧道 ID: [{string.Join(", ", options.ProxyIds)}]");
        }

        Logger.Info($"开始启动隧道，数量: {options.ProxyIds.Count}");
        var result = await StartProxiesAsync(frpcPath, frpToken!, options.ProxyIds);
        Logger.MethodExit($"{result}");
        return result;
    }

    /// <summary>
    /// 交互式登录流程
    /// </summary>
    private async Task<bool> InteractiveLoginAsync()
    {
        Logger.MethodEntry();
        
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("                     欢迎使用 Kairo CLI");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();
        Console.WriteLine("检测到您尚未登录，需要先完成 OAuth 授权。");
        Console.WriteLine();

        var oauthUrl = $"{ApiEndpoints.OAuthAuthorize}?client_id={AppConstants.APPID}&scopes=User,Node,Tunnel,Sign&mode=code";
        Logger.Debug($"OAuth URL: {oauthUrl}");
        
        Console.WriteLine("请在浏览器中打开以下链接进行授权:");
        Console.WriteLine();
        Console.WriteLine($"  {oauthUrl}");
        Console.WriteLine();
        
        // 尝试自动打开浏览器
        Console.Write("是否自动打开浏览器? [Y/n]: ");
        var autoOpen = Console.ReadLine()?.Trim().ToLowerInvariant();
        Logger.Debug($"用户输入自动打开浏览器: '{autoOpen}'");
        
        if (autoOpen != "n" && autoOpen != "no")
        {
            Logger.Debug("尝试自动打开浏览器");
            TryOpenBrowser(oauthUrl);
        }
        
        Console.WriteLine();
        Console.WriteLine("授权完成后，请将获取到的授权码粘贴到下方:");
        Console.Write("授权码: ");
        
        var code = Console.ReadLine()?.Trim();
        Logger.Debug($"用户输入授权码长度: {code?.Length ?? 0}");
        
        if (string.IsNullOrWhiteSpace(code))
        {
            Logger.Warning("授权码为空");
            Console.WriteLine("[错误] 授权码不能为空");
            Logger.MethodExit("false (授权码为空)");
            return false;
        }

        var result = await PerformLoginWithCodeAsync(code);
        Logger.MethodExit(result.ToString());
        return result;
    }

    /// <summary>
    /// 使用授权码登录
    /// </summary>
    private async Task<bool> PerformLoginWithCodeAsync(string code)
    {
        Logger.MethodEntry($"code长度={code.Length}");
        
        Console.WriteLine("[信息] 正在使用 OAuth Code 获取令牌...");
        _apiClient = new ApiClient();
        
        Logger.Debug("调用 ExchangeCodeForRefreshTokenAsync");
        var loginResult = await _apiClient.ExchangeCodeForRefreshTokenAsync(code);
        
        if (!loginResult.Success)
        {
            Logger.Error($"登录失败: {loginResult.Message}");
            Console.WriteLine($"[错误] 登录失败: {loginResult.Message}");
            Console.WriteLine();
            Console.WriteLine("[提示] 请重新获取授权码:");
            ShowOAuthUrl();
            Logger.MethodExit("false");
            return false;
        }
        
        Logger.Info($"登录成功，用户: {loginResult.Username}");
        Console.WriteLine($"[成功] 登录成功! 用户: {loginResult.Username}");
        Console.WriteLine($"[信息] 凭据已保存到配置文件");
        Console.WriteLine();
        Logger.MethodExit("true");
        return true;
    }

    /// <summary>
    /// 使用 Refresh Token 登录
    /// </summary>
    private async Task<bool> PerformLoginWithRefreshTokenAsync(string refreshToken)
    {
        Logger.MethodEntry($"refreshToken长度={refreshToken.Length}");
        
        Console.WriteLine("[信息] 正在使用 Refresh Token 进行登录...");
        _apiClient = new ApiClient();
        
        Logger.Debug("调用 LoginWithRefreshTokenAsync");
        var loginResult = await _apiClient.LoginWithRefreshTokenAsync(refreshToken);
        
        if (!loginResult.Success)
        {
            Logger.Error($"Refresh Token 登录失败: {loginResult.Message}");
            Console.WriteLine($"[错误] 登录失败: {loginResult.Message}");
            Console.WriteLine();
            Console.WriteLine("[提示] Refresh Token 可能已过期，请重新获取授权码:");
            ShowOAuthUrl();
            Logger.MethodExit("false");
            return false;
        }
        
        Logger.Info($"登录成功，用户: {loginResult.Username}");
        Console.WriteLine($"[成功] 登录成功! 用户: {loginResult.Username}");
        Console.WriteLine($"[信息] 凭据已保存到配置文件");
        Console.WriteLine();
        Logger.MethodExit("true");
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
        Logger.MethodEntry($"可选隧道数量={tunnels.Count}");
        
        Console.WriteLine("请选择要启动的隧道:");
        Console.WriteLine("  - 输入序号 (例如: 1,2,3) 或隧道 ID");
        Console.WriteLine("  - 输入 'all' 或直接按回车启动全部隧道");
        Console.WriteLine("  - 输入 'q' 退出");
        Console.WriteLine();
        Console.Write("您的选择: ");
        
        var input = Console.ReadLine()?.Trim().ToLowerInvariant();
        Logger.Debug($"用户输入: '{input}'");
        
        if (input == "q" || input == "quit" || input == "exit")
        {
            Logger.Info("用户取消选择");
            Console.WriteLine("[信息] 已取消");
            return Task.FromResult<List<int>?>(null);
        }

        var selectedIds = new List<int>();
        
        if (string.IsNullOrEmpty(input) || input == "all" || input == "a")
        {
            // 启动全部
            Logger.Debug("用户选择启动全部隧道");
            foreach (var tunnel in tunnels)
                selectedIds.Add(tunnel.Id);
            Console.WriteLine($"[信息] 已选择全部 {selectedIds.Count} 个隧道");
        }
        else
        {
            // 解析输入
            Logger.Debug($"解析用户输入: {input}");
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
                        {
                            Logger.Debug($"添加隧道 ID: {num}");
                            selectedIds.Add(num);
                        }
                        else if (tunnel == null)
                        {
                            Logger.Warning($"未找到隧道 ID: {num}");
                            Console.WriteLine($"[警告] 未找到隧道 ID: {num}");
                        }
                    }
                }
            }
        }

        Logger.MethodExit($"选中{selectedIds.Count}个: [{string.Join(", ", selectedIds)}]");
        return Task.FromResult<List<int>?>(selectedIds);
    }

    /// <summary>
    /// 尝试打开浏览器
    /// </summary>
    private void TryOpenBrowser(string url)
    {
        Logger.MethodEntry($"url={url}");
        try
        {
            if (OperatingSystem.IsLinux())
            {
                Logger.Debug("使用 xdg-open 打开浏览器 (Linux)");
                Logger.ProcessStart("xdg-open", url);
                using var p = Process.Start(new ProcessStartInfo("xdg-open", url) { UseShellExecute = false });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Logger.Debug("使用 open 打开浏览器 (macOS)");
                Logger.ProcessStart("open", url);
                using var p = Process.Start(new ProcessStartInfo("open", url) { UseShellExecute = false });
            }
            else if (OperatingSystem.IsWindows())
            {
                Logger.Debug("使用 cmd /c start 打开浏览器 (Windows)");
                var escapedUrl = url.Replace("&", "^&");
                Logger.ProcessStart("cmd", $"/c start {escapedUrl}");
                using var p = Process.Start(new ProcessStartInfo("cmd", $"/c start {escapedUrl}") { UseShellExecute = false });
            }
            Logger.Info("已尝试打开浏览器");
            Console.WriteLine("[信息] 已尝试打开浏览器");
        }
        catch (Exception ex)
        {
            Logger.Exception(ex, "无法打开浏览器");
            Console.WriteLine("[提示] 无法自动打开浏览器，请手动复制链接");
        }
        Logger.MethodExit();
    }

    private async Task<int> StartProxiesAsync(string frpcPath, string frpToken, List<int> proxyIds)
    {
        Logger.MethodEntry($"frpcPath={frpcPath}, proxyIds.Count={proxyIds.Count}");
        
        Console.WriteLine();
        Console.WriteLine($"[信息] 正在启动 {proxyIds.Count} 个隧道...");
        Console.WriteLine("[信息] 按 Ctrl+C 停止所有隧道");
        Console.WriteLine();

        // 防止重复注册事件处理器
        if (!_cancelKeyRegistered)
        {
            _cancelKeyRegistered = true;
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                Logger.Info("用户按下 Ctrl+C，开始停止隧道");
                Console.WriteLine();
                Console.WriteLine("[信息] 正在停止所有隧道...");
                _cts.Cancel();
            };
        }

        var processes = _frpcProcesses;

        foreach (var proxyId in proxyIds)
        {
            Logger.Debug($"启动隧道 ID: {proxyId}");
            var process = StartFrpcProcess(frpcPath, frpToken, proxyId);
            if (process != null)
            {
                lock (_frpcProcesses)
                {
                    processes.Add(process);
                }
                Logger.Info($"隧道 {proxyId} 启动成功，PID: {process.Id}");
                Console.WriteLine($"[成功] 隧道 {proxyId} 已启动 (PID: {process.Id})");
            }
            else
            {
                Logger.Error($"隧道 {proxyId} 启动失败");
                Console.WriteLine($"[错误] 隧道 {proxyId} 启动失败");
            }
        }

        Logger.Info($"成功启动 {processes.Count}/{proxyIds.Count} 个隧道");

        if (processes.Count == 0)
        {
            Logger.Error("没有成功启动的隧道");
            Console.WriteLine("[错误] 没有成功启动的隧道");
            Logger.MethodExit("1");
            return 1;
        }

        Logger.Debug("进入隧道监控循环");
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                bool allExited = processes.All(p => p.HasExited);
                if (allExited)
                {
                    Logger.Info("所有隧道进程已退出");
                    Console.WriteLine("[信息] 所有隧道进程已退出");
                    break;
                }
                await Task.Delay(1000, _cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Debug("监控循环被取消");
        }

        Logger.Debug("开始终止所有进程");
        // Use KillAllFrpcProcesses for centralized cleanup (also clears the list)
        KillAllFrpcProcesses();

        Logger.Info("所有隧道已停止");
        Console.WriteLine("[信息] 所有隧道已停止");
        Logger.MethodExit("0");
        return 0;
    }

    private Process? StartFrpcProcess(string frpcPath, string frpToken, int proxyId)
    {
        Logger.MethodEntry($"proxyId={proxyId}");
        try
        {
            var arguments = $"-u {frpToken} -t {proxyId}";
            Logger.ProcessStart(frpcPath, arguments);
            
            var psi = new ProcessStartInfo
            {
                FileName = frpcPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            var process = new Process { StartInfo = psi };
            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    Logger.Debug($"[隧道 {proxyId} stdout] {e.Data}");
                    Console.WriteLine($"[隧道 {proxyId}] {e.Data}");
                }
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    Logger.Warning($"[隧道 {proxyId} stderr] {e.Data}");
                    Console.WriteLine($"[隧道 {proxyId} 错误] {e.Data}");
                }
            };

            if (!process.Start())
            {
                Logger.Error($"进程启动返回 false");
                Logger.MethodExit("null");
                return null;
            }

            Logger.Debug($"frpc 进程已启动，PID: {process.Id}");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            Logger.MethodExit($"PID={process.Id}");
            return process;
        }
        catch (Exception ex)
        {
            Logger.Exception(ex, $"启动 frpc 失败 (proxyId={proxyId})");
            Console.WriteLine($"[错误] 启动 frpc 失败: {ex.Message}");
            Logger.MethodExit("null (exception)");
            return null;
        }
    }

    private CliOptions ParseArguments()
    {
        Logger.MethodEntry();
        var options = new CliOptions();
        bool noInteractive = false;

        for (int i = 0; i < _args.Length; i++)
        {
            var arg = _args[i];
            Logger.Debug($"解析参数[{i}]: {arg}");
            switch (arg.ToLowerInvariant())
            {
                case "--help" or "-h":
                    options.ShowHelp = true;
                    Logger.Debug("  -> ShowHelp = true");
                    break;
                case "--version" or "-v":
                    options.ShowVersion = true;
                    Logger.Debug("  -> ShowVersion = true");
                    break;
                case "--oauth" or "--get-oauth-url":
                    options.GetOAuthUrl = true;
                    Logger.Debug("  -> GetOAuthUrl = true");
                    break;
                case "--code":
                    if (i + 1 < _args.Length)
                    {
                        options.OAuthCode = _args[++i];
                        Logger.Debug($"  -> OAuthCode = (length={options.OAuthCode.Length})");
                    }
                    else
                    {
                        Logger.Warning("  -> --code 缺少参数值");
                    }
                    break;
                case "--refresh-token" or "-r":
                    if (i + 1 < _args.Length)
                    {
                        options.RefreshToken = _args[++i];
                        Logger.Debug($"  -> RefreshToken = (length={options.RefreshToken.Length})");
                    }
                    else
                    {
                        Logger.Warning("  -> --refresh-token 缺少参数值");
                    }
                    break;
                case "--frp-token" or "-t":
                    if (i + 1 < _args.Length)
                    {
                        options.FrpToken = _args[++i];
                        Logger.Debug($"  -> FrpToken = (length={options.FrpToken.Length})");
                    }
                    else
                    {
                        Logger.Warning("  -> --frp-token 缺少参数值");
                    }
                    break;
                case "--frpc-path" or "-f":
                    if (i + 1 < _args.Length)
                    {
                        options.FrpcPath = _args[++i];
                        Logger.Debug($"  -> FrpcPath = {options.FrpcPath}");
                    }
                    else
                    {
                        Logger.Warning("  -> --frpc-path 缺少参数值");
                    }
                    break;
                case "--proxy" or "-p":
                    if (i + 1 < _args.Length)
                    {
                        var proxyArg = _args[++i];
                        Logger.Debug($"  -> 解析隧道 ID: {proxyArg}");
                        foreach (var part in proxyArg.Split(',', StringSplitOptions.RemoveEmptyEntries))
                        {
                            if (int.TryParse(part.Trim(), out var id))
                            {
                                options.ProxyIds.Add(id);
                                Logger.Debug($"     添加隧道 ID: {id}");
                            }
                            else
                            {
                                Logger.Warning($"     无效的隧道 ID: {part}");
                            }
                        }
                    }
                    else
                    {
                        Logger.Warning("  -> --proxy 缺少参数值");
                    }
                    break;
                case "--list" or "-l":
                    options.ListProxies = true;
                    Logger.Debug("  -> ListProxies = true");
                    break;
                case "--no-interactive":
                    noInteractive = true;
                    Logger.Debug("  -> noInteractive = true");
                    break;
                case "--debug" or "-d":
                    // 已在 Program.cs 中处理
                    Logger.Debug("  -> 调试模式 (已在 Program.cs 中处理)");
                    break;
                case "--log-file":
                    // 已在 Program.cs 中处理
                    Logger.Debug("  -> 文件日志 (已在 Program.cs 中处理)");
                    break;
                case "--quiet" or "-q":
                    // 已在 Program.cs 中处理
                    Logger.Debug("  -> 安静模式 (已在 Program.cs 中处理)");
                    break;
                case "--github" or "--no-mirror":
                    options.ForceGitHub = true;
                    Logger.Debug("  -> ForceGitHub = true (强制使用 GitHub 源)");
                    break;
                default:
                    Logger.Debug($"  -> 未知参数: {arg}");
                    break;
            }
        }

        // 如果没有禁用交互模式且标准输入可用，则启用交互模式
        if (!noInteractive && !Console.IsInputRedirected)
        {
            options.InteractiveMode = true;
            Logger.Debug("InteractiveMode = true (标准输入未重定向)");
        }
        else if (Console.IsInputRedirected)
        {
            Logger.Debug("InteractiveMode = false (标准输入已重定向)");
        }
        else
        {
            Logger.Debug("InteractiveMode = false (用户禁用)");
        }

        Logger.MethodExit();
        return options;
    }

    private void ShowHelp()
    {
        Logger.MethodEntry();
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
        Console.WriteLine("  --debug, -d               启用调试日志模式");
        Console.WriteLine("  --log-file                将日志写入文件");
        Console.WriteLine("  --quiet, -q               安静模式（只显示警告和错误）");
        Console.WriteLine("  --github, --no-mirror     强制使用 GitHub 下载源");
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
        Console.WriteLine();
        Console.WriteLine("  调试模式:");
        Console.WriteLine("    kairo-cli --debug          # 显示详细日志");
        Console.WriteLine("    kairo-cli --debug --log-file  # 详细日志并写入文件");
        Logger.MethodExit();
    }

    private void ShowVersion()
    {
        Console.WriteLine($"Kairo CLI {AppConstants.Version} ({AppConstants.Branch.ToDisplayName()})");
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
        public bool ForceGitHub { get; set; }
        public string? OAuthCode { get; set; }
        public string? RefreshToken { get; set; }
        public string? FrpToken { get; set; }
        public string? FrpcPath { get; set; }
        public List<int> ProxyIds { get; } = new();
    }
}
