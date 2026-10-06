using System.Diagnostics;
using System.Runtime.InteropServices;
using Kairo.Core.Localization;
using Kairo.Core.Logging;
using Kairo.Core.Models;
using Kairo.Core.Providers;
using Kairo.Cli.Services;
using Kairo.Cli.Utils;

namespace Kairo.Cli;

internal sealed class CliFrpcProcessRunner : IDisposable
{
    private static readonly ConsoleColor[] PrefixColors =
    {
        ConsoleColor.Cyan, ConsoleColor.Magenta, ConsoleColor.Blue, ConsoleColor.Green, ConsoleColor.Yellow, ConsoleColor.DarkCyan
    };

    private readonly Func<IFrpProvider> _providerFactory;
    private readonly CancellationTokenSource _cts;
    private readonly List<Process> _processes = new();
    private readonly object _outputLock = new();
    private bool _cancelKeyRegistered;
    private PosixSignalRegistration? _sigTermRegistration;
    private int _stopRequested;
    private volatile bool _stopping;

    public CliFrpcProcessRunner(Func<IFrpProvider> providerFactory, CancellationTokenSource cts)
    {
        _providerFactory = providerFactory;
        _cts = cts;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => KillAll();
    }

    public void Dispose()
    {
        _sigTermRegistration?.Dispose();
        _sigTermRegistration = null;
        KillAll();
    }

    public async Task<int> StartAsync(string frpcPath, string frpToken, List<int> proxyIds, List<Tunnel>? tunnels, ApiClient apiClient)
    {
        ConsoleUi.Section(L.Plural("cli.run.title", proxyIds.Count));
        RegisterCancelHandler();

        for (var i = 0; i < proxyIds.Count; i++)
            await StartOneAsync(frpcPath, frpToken, proxyIds[i], tunnels, apiClient, PrefixColors[i % PrefixColors.Length]);

        int running;
        lock (_processes) running = _processes.Count;
        Logger.Info($"成功启动 {running}/{proxyIds.Count} 个隧道");
        if (running == 0)
        {
            ConsoleUi.Error(L.T("cli.run.noneStarted"));
            return 1;
        }

        Console.WriteLine();
        if (running < proxyIds.Count)
            ConsoleUi.Warn(L.T("cli.run.partlyStarted", running, proxyIds.Count));
        else
            ConsoleUi.Success(L.Plural("cli.run.allStarted", running));
        ConsoleUi.Dim(L.T("cli.run.stopHint"));
        Console.WriteLine();

        var allExitedOnTheirOwn = false;
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                bool allExited;
                lock (_processes) allExited = _processes.All(p => p.HasExited);
                if (allExited)
                {
                    allExitedOnTheirOwn = true;
                    break;
                }
                await Task.Delay(1000, _cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Debug("收到停止信号");
        }

        _stopping = true;
        KillAll();
        if (allExitedOnTheirOwn)
        {
            ConsoleUi.Error(L.T("cli.run.allExited"));
            return 1;
        }

        ConsoleUi.Success(L.T("cli.run.allStopped"));
        return 0;
    }

    public void KillAll()
    {
        _stopping = true;
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

    private async Task StartOneAsync(string frpcPath, string frpToken, int proxyId, List<Tunnel>? tunnels, ApiClient apiClient, ConsoleColor color)
    {
        var provider = _providerFactory();
        var tunnel = tunnels?.FirstOrDefault(t => t.Id == proxyId);
        var label = tunnel == null ? $"#{proxyId}" : $"{tunnel.ProxyName} (#{proxyId})";

        if (tunnels != null && tunnel == null)
        {
            ConsoleUi.Error(L.T("cli.run.notOnAccount", label));
            return;
        }

        var token = frpToken;
        if (provider.Type == FrpProviderType.Lolia)
        {
            if (tunnel == null)
            {
                ConsoleUi.Error(L.T("cli.run.noTunnelInfo", label));
                return;
            }

            var config = await apiClient.GetFrpcConfigAsync(tunnel);
            if (!config.Success || string.IsNullOrWhiteSpace(config.Data?.Token))
            {
                ConsoleUi.Error(L.T("cli.run.failedWithReason", label, config.Message));
                return;
            }
            token = config.Data.Token;
        }

        var prefix = BuildPrefix(proxyId, tunnel?.ProxyName);
        var process = StartProcess(provider, frpcPath, token, proxyId, tunnel?.ProxyName ?? string.Empty, prefix, color);
        if (process == null)
        {
            ConsoleUi.Error(L.T("cli.run.failed", label));
            return;
        }

        lock (_processes)
            _processes.Add(process);

        var address = tunnel == null ? null : TunnelAddress.GetPublicAddress(tunnel);
        ConsoleUi.Success(string.IsNullOrEmpty(address) || address == "-"
            ? L.T("cli.run.started", label, process.Id)
            : L.T("cli.run.startedWithAddress", label, process.Id, address));
    }

    private Process? StartProcess(IFrpProvider provider, string frpcPath, string frpToken, int proxyId, string proxyName, string prefix, ConsoleColor color)
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
            var maskedArguments = SecretMasker.Redact(arguments, frpToken);
            Logger.Debug($"[FRPC] 启动参数: provider={provider.Id}, path=\"{frpcPath}\", args={maskedArguments}");
            Logger.ProcessStart(frpcPath, maskedArguments);

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
                },
                EnableRaisingEvents = true
            };
            process.OutputDataReceived += (_, e) => WriteProcessLine(prefix, color, proxyId, e.Data, error: false);
            process.ErrorDataReceived += (_, e) => WriteProcessLine(prefix, color, proxyId, e.Data, error: true);
            process.Exited += (_, _) => OnProcessExited(process, prefix, proxyId);
            if (!process.Start()) return null;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return process;
        }
        catch (Exception ex)
        {
            Logger.Exception(ex, $"启动 frpc 失败 (proxyId={proxyId})");
            ConsoleUi.Error(L.T("cli.run.frpcFailed", ex.Message));
            return null;
        }
    }

    private void OnProcessExited(Process process, string prefix, int proxyId)
    {
        if (_stopping) return;
        int? exitCode = null;
        try
        {
            // Exited 可能早于重定向输出读取完毕触发，等待输出读完，保证退出提示出现在最后
            process.WaitForExit();
            exitCode = process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            // 进程对象已释放
        }
        if (_stopping) return;
        Logger.Info($"隧道 {proxyId} 的 frpc 进程已退出，退出码 {exitCode?.ToString() ?? "未知"}");
        lock (_outputLock)
            ConsoleUi.Warn(exitCode.HasValue
                ? L.T("cli.run.exitedWithCode", prefix.Trim('[', ']'), exitCode)
                : L.T("cli.run.exited", prefix.Trim('[', ']')));
    }

    private void WriteProcessLine(string prefix, ConsoleColor color, int proxyId, string? line, bool error)
    {
        if (string.IsNullOrEmpty(line)) return;
        if (error)
            Logger.Debug($"[隧道 {proxyId} stderr] {line}");
        else
            Logger.Debug($"[隧道 {proxyId} stdout] {line}");

        lock (_outputLock)
        {
            ConsoleUi.Write(prefix, error ? ConsoleColor.Red : color);
            Console.WriteLine(" " + line);
        }
    }

    private static string BuildPrefix(int proxyId, string? name) =>
        string.IsNullOrWhiteSpace(name) ? $"[#{proxyId}]" : $"[{ConsoleUi.Truncate(name, 16)}#{proxyId}]";

    private void RegisterCancelHandler()
    {
        if (_cancelKeyRegistered) return;
        _cancelKeyRegistered = true;
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            RequestStop();
        };

        // SIGTERM（kill、systemd、docker stop、启动器转发）默认会直接结束进程且不触发 ProcessExit，
        // frpc 会变成孤儿进程继续运行，因此与 Ctrl+C 走同样的停止流程
        try
        {
            _sigTermRegistration = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
            {
                context.Cancel = true;
                RequestStop();
            });
        }
        catch (Exception ex)
        {
            Logger.Debug($"无法注册 SIGTERM 处理: {ex.Message}");
        }
    }

    private void RequestStop()
    {
        if (Interlocked.Exchange(ref _stopRequested, 1) == 1) return;
        _stopping = true;
        Console.WriteLine();
        ConsoleUi.Info(L.T("cli.run.stopping"));
        try { _cts.Cancel(); }
        catch (ObjectDisposedException)
        {
            // 已在退出流程中
        }
    }
}
