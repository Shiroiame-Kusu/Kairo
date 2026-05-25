using System.Diagnostics;
using Kairo.Core.Models;
using Kairo.Core.Providers;
using Kairo.Cli.Services;
using Kairo.Cli.Utils;

namespace Kairo.Cli;

internal sealed class CliFrpcProcessRunner : IDisposable
{
    private readonly Func<IFrpProvider> _providerFactory;
    private readonly CancellationTokenSource _cts;
    private readonly List<Process> _processes = new();
    private bool _cancelKeyRegistered;

    public CliFrpcProcessRunner(Func<IFrpProvider> providerFactory, CancellationTokenSource cts)
    {
        _providerFactory = providerFactory;
        _cts = cts;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => KillAll();
    }

    public void Dispose() => KillAll();

    public async Task<int> StartAsync(string frpcPath, string frpToken, List<int> proxyIds, List<Tunnel>? tunnels, ApiClient apiClient)
    {
        Console.WriteLine();
        Console.WriteLine($"[信息] 正在启动 {proxyIds.Count} 个隧道...");
        Console.WriteLine("[信息] 按 Ctrl+C 停止所有隧道");
        Console.WriteLine();

        RegisterCancelHandler();
        foreach (var proxyId in proxyIds)
            await StartOneAsync(frpcPath, frpToken, proxyId, tunnels, apiClient);

        Logger.Info($"成功启动 {_processes.Count}/{proxyIds.Count} 个隧道");
        if (_processes.Count == 0)
        {
            Console.WriteLine("[错误] 没有成功启动的隧道");
            return 1;
        }

        try
        {
            while (!_cts.IsCancellationRequested)
            {
                if (_processes.All(p => p.HasExited))
                {
                    Console.WriteLine("[信息] 所有隧道进程已退出");
                    break;
                }
                await Task.Delay(1000, _cts.Token);
            }
        }
        catch (OperationCanceledException ex)
        {
            Kairo.Cli.Utils.Logger.Exception(ex, "Unhandled exception in Kairo.Cli/App/CliFrpcProcessRunner.cs:55");
        }

        KillAll();
        Console.WriteLine("[信息] 所有隧道已停止");
        return 0;
    }

    public void KillAll()
    {
        List<Process> snapshot;
        lock (_processes)
        {
            snapshot = new List<Process>(_processes);
            _processes.Clear();
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
            catch (System.Exception ex)
            {
                Kairo.Cli.Utils.Logger.Exception(ex, "Unhandled exception in Kairo.Cli/App/CliFrpcProcessRunner.cs:83");
            }
            finally
            {
                try { proc.Dispose(); }
                catch (System.Exception ex)
                {
                    Kairo.Cli.Utils.Logger.Exception(ex, "Unhandled exception in Kairo.Cli/App/CliFrpcProcessRunner.cs:86");
                }
            }
        }
    }

    private async Task StartOneAsync(string frpcPath, string frpToken, int proxyId, List<Tunnel>? tunnels, ApiClient apiClient)
    {
        var provider = _providerFactory();
        var tunnel = tunnels?.FirstOrDefault(t => t.Id == proxyId);
        var token = frpToken;
        if (provider.Type == FrpProviderType.Lolia)
        {
            if (tunnel == null)
            {
                Console.WriteLine($"[错误] 隧道 {proxyId} 启动失败: 未找到隧道信息");
                return;
            }

            var config = await apiClient.GetFrpcConfigAsync(tunnel);
            if (!config.Success || string.IsNullOrWhiteSpace(config.Data?.Token))
            {
                Console.WriteLine($"[错误] 隧道 {proxyId} 启动失败: {config.Message}");
                return;
            }
            token = config.Data.Token;
        }

        var process = StartProcess(provider, frpcPath, token, proxyId, tunnel?.ProxyName ?? string.Empty);
        if (process == null)
        {
            Console.WriteLine($"[错误] 隧道 {proxyId} 启动失败");
            return;
        }

        lock (_processes)
            _processes.Add(process);
        Console.WriteLine($"[成功] 隧道 {proxyId} 已启动 (PID: {process.Id})");
    }

    private static Process? StartProcess(IFrpProvider provider, string frpcPath, string frpToken, int proxyId, string proxyName)
    {
        try
        {
            var arguments = provider.BuildFrpcArguments(new FrpStartOptions
            {
                TunnelId = proxyId,
                TunnelName = proxyName,
                FrpToken = frpToken,
                ApiBaseUrl = provider.ApiBaseUrl
            });
            Logger.Info($"[FRPC] 启动参数: provider={provider.Id}, path=\"{frpcPath}\", args={arguments}");
            Logger.ProcessStart(frpcPath, arguments);

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = frpcPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            process.OutputDataReceived += (_, e) => WriteProcessLine(proxyId, e.Data, error: false);
            process.ErrorDataReceived += (_, e) => WriteProcessLine(proxyId, e.Data, error: true);
            if (!process.Start()) return null;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return process;
        }
        catch (Exception ex)
        {
            Logger.Exception(ex, $"启动 frpc 失败 (proxyId={proxyId})");
            Console.WriteLine($"[错误] 启动 frpc 失败: {ex.Message}");
            return null;
        }
    }

    private static void WriteProcessLine(int proxyId, string? line, bool error)
    {
        if (string.IsNullOrEmpty(line)) return;
        if (error)
        {
            Logger.Warning($"[隧道 {proxyId} stderr] {line}");
            Console.WriteLine($"[隧道 {proxyId} 错误] {line}");
        }
        else
        {
            Logger.Debug($"[隧道 {proxyId} stdout] {line}");
            Console.WriteLine($"[隧道 {proxyId}] {line}");
        }
    }

    private void RegisterCancelHandler()
    {
        if (_cancelKeyRegistered) return;
        _cancelKeyRegistered = true;
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Console.WriteLine();
            Console.WriteLine("[信息] 正在停止所有隧道...");
            _cts.Cancel();
        };
    }
}
