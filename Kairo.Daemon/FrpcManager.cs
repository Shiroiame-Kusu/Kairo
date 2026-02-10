using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Kairo.Core.Daemon;

namespace Kairo.Daemon;

/// <summary>
/// frpc 进程管理器 — 管理所有 frpc 子进程，输出重定向到日志文件
/// </summary>
internal sealed class FrpcManager : IDisposable
{
    private sealed class ProxyProcess
    {
        public int ProxyId { get; init; }
        public Process Process { get; init; } = null!;
        public DateTime StartTime { get; init; }
        public string FrpcPath { get; init; } = "";
        public string LogFile { get; init; } = "";
        public StreamWriter? LogWriter { get; set; }
    }

    private readonly ConcurrentDictionary<int, ProxyProcess> _proxies = new();
    private readonly object _writeLock = new();
    private bool _disposed;

    /// <summary>隧道退出事件</summary>
    public event Action<int>? ProxyExited;

    /// <summary>日志行事件（实时推送给已订阅的客户端）</summary>
    public event Action<LogLine>? LogEmitted;

    /// <summary>
    /// 启动隧道
    /// </summary>
    public ProxyState StartProxy(int proxyId, string frpcPath, string frpToken)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FrpcManager));

        if (string.IsNullOrWhiteSpace(frpcPath) || !File.Exists(frpcPath))
            throw new FileNotFoundException($"frpc 路径无效: {frpcPath}");

        if (_proxies.ContainsKey(proxyId))
            throw new InvalidOperationException($"隧道 {proxyId} 已在运行中");

        // 确保 Unix 上的执行权限
        EnsureExecutePermission(frpcPath);

        // 准备日志文件
        var logDir = DaemonConstants.GetLogDirectory();
        Directory.CreateDirectory(logDir);
        var logFile = DaemonConstants.GetProxyLogPath(proxyId);
        var logWriter = new StreamWriter(logFile, append: true, Encoding.UTF8)
        {
            AutoFlush = true
        };

        var psi = new ProcessStartInfo
        {
            FileName = frpcPath,
            Arguments = $"-u {frpToken} -t {proxyId}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var startTime = DateTime.UtcNow;

        var proxyProc = new ProxyProcess
        {
            ProxyId = proxyId,
            Process = proc,
            StartTime = startTime,
            FrpcPath = frpcPath,
            LogFile = logFile,
            LogWriter = logWriter
        };

        // 输出处理 → 写日志文件 + 触发事件
        proc.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            var line = new LogLine
            {
                Timestamp = DateTime.UtcNow,
                ProxyId = proxyId,
                Text = e.Data,
                IsError = false
            };
            WriteLog(proxyProc, line);
        };

        proc.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            var line = new LogLine
            {
                Timestamp = DateTime.UtcNow,
                ProxyId = proxyId,
                Text = e.Data,
                IsError = true
            };
            WriteLog(proxyProc, line);
        };

        proc.Exited += (_, _) =>
        {
            _proxies.TryRemove(proxyId, out _);
            var exitLine = new LogLine
            {
                Timestamp = DateTime.UtcNow,
                ProxyId = proxyId,
                Text = $"[daemon] 进程已退出，ExitCode={TryGetExitCode(proc)}",
                IsError = false
            };
            WriteLog(proxyProc, exitLine);

            // 关闭日志写入器
            try { proxyProc.LogWriter?.Dispose(); proxyProc.LogWriter = null; } catch { }

            Console.WriteLine($"[frpc-manager] Proxy {proxyId} 已退出");
            try { ProxyExited?.Invoke(proxyId); } catch { }
        };

        if (!proc.Start())
            throw new InvalidOperationException($"frpc 启动失败 (proxyId={proxyId})");

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        if (!_proxies.TryAdd(proxyId, proxyProc))
        {
            // 竞态：另一个线程已经添加了同一个 proxyId
            try { proc.Kill(true); } catch { }
            throw new InvalidOperationException($"隧道 {proxyId} 启动竞争失败");
        }

        Console.WriteLine($"[frpc-manager] 启动隧道 {proxyId}, PID={proc.Id}");

        return new ProxyState
        {
            ProxyId = proxyId,
            Pid = proc.Id,
            StartTime = startTime,
            Running = true,
            FrpcPath = frpcPath,
            LogFile = logFile
        };
    }

    /// <summary>
    /// 停止隧道
    /// </summary>
    public bool StopProxy(int proxyId)
    {
        if (!_proxies.TryRemove(proxyId, out var pp)) return false;

        try
        {
            if (!pp.Process.HasExited)
            {
                pp.Process.Kill(true);
                pp.Process.WaitForExit(3000);
            }
        }
        catch { }
        finally
        {
            try { pp.LogWriter?.Dispose(); pp.LogWriter = null; } catch { }
            try { pp.Process.Dispose(); } catch { }
        }

        Console.WriteLine($"[frpc-manager] 已停止隧道 {proxyId}");
        return true;
    }

    /// <summary>
    /// 停止所有隧道
    /// </summary>
    public int StopAll()
    {
        var ids = _proxies.Keys.ToList();
        int count = 0;
        foreach (var id in ids)
        {
            if (StopProxy(id)) count++;
        }
        return count;
    }

    /// <summary>
    /// 获取所有正在运行的隧道状态
    /// </summary>
    public List<ProxyState> GetAllStatus()
    {
        var result = new List<ProxyState>();
        foreach (var (id, pp) in _proxies)
        {
            var running = !pp.Process.HasExited;
            result.Add(new ProxyState
            {
                ProxyId = id,
                Pid = TryGetPid(pp.Process),
                StartTime = pp.StartTime,
                Running = running,
                FrpcPath = pp.FrpcPath,
                LogFile = pp.LogFile
            });

            // 清理已退出但未被 Exited 事件处理的进程
            if (!running)
            {
                _proxies.TryRemove(id, out _);
                try { pp.LogWriter?.Dispose(); pp.LogWriter = null; } catch { }
            }
        }
        return result;
    }

    /// <summary>
    /// 检查指定隧道是否在运行
    /// </summary>
    public bool IsRunning(int proxyId)
    {
        if (_proxies.TryGetValue(proxyId, out var pp))
        {
            if (!pp.Process.HasExited) return true;
            // 进程已退出，清理
            _proxies.TryRemove(proxyId, out _);
        }
        return false;
    }

    /// <summary>
    /// 获取隧道的历史日志
    /// </summary>
    public List<LogLine> GetLogHistory(int proxyId, int maxLines = 200)
    {
        var logFile = DaemonConstants.GetProxyLogPath(proxyId);
        if (!File.Exists(logFile)) return [];

        try
        {
            // 从文件尾部读取最后 N 行
            var lines = ReadLastLines(logFile, maxLines);
            return lines.Select(text => new LogLine
            {
                Timestamp = DateTime.UtcNow, // 日志文件中的时间戳需要解析
                ProxyId = proxyId,
                Text = text,
                IsError = text.Contains("[E]") || text.Contains("[ERROR]")
            }).ToList();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// 尝试接管已有的 frpc 进程（daemon 重启时恢复状态）
    /// </summary>
    public bool TryAdoptProcess(ProxyState state)
    {
        try
        {
            var proc = Process.GetProcessById(state.Pid);

            // 验证这确实是 frpc 进程
            var procName = proc.ProcessName.ToLowerInvariant();
            if (!procName.Contains("frpc"))
            {
                Console.WriteLine($"[frpc-manager] PID {state.Pid} 不是 frpc 进程 (name={proc.ProcessName})，跳过");
                return false;
            }

            // 注意：接管的进程无法 redirect stdout/stderr（管道已丢失）
            // 但我们仍可以监控进程是否存活，并且日志文件中可能已有之前的输出

            var logFile = state.LogFile;
            StreamWriter? logWriter = null;
            try
            {
                if (!string.IsNullOrEmpty(logFile))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(logFile)!);
                    logWriter = new StreamWriter(logFile, append: true, Encoding.UTF8) { AutoFlush = true };
                    logWriter.WriteLine($"[{DateTime.UtcNow:O}] [daemon] 已接管进程 PID={state.Pid}");
                }
            }
            catch { }

            var pp = new ProxyProcess
            {
                ProxyId = state.ProxyId,
                Process = proc,
                StartTime = state.StartTime,
                FrpcPath = state.FrpcPath,
                LogFile = logFile,
                LogWriter = logWriter
            };

            proc.EnableRaisingEvents = true;
            proc.Exited += (_, _) =>
            {
                _proxies.TryRemove(state.ProxyId, out _);
                try
                {
                    pp.LogWriter?.WriteLine($"[{DateTime.UtcNow:O}] [daemon] 被接管的进程已退出");
                    pp.LogWriter?.Dispose();
                    pp.LogWriter = null;
                }
                catch { }
                Console.WriteLine($"[frpc-manager] 被接管的 Proxy {state.ProxyId} (PID={state.Pid}) 已退出");
                try { ProxyExited?.Invoke(state.ProxyId); } catch { }
            };

            if (!_proxies.TryAdd(state.ProxyId, pp))
            {
                Console.WriteLine($"[frpc-manager] 隧道 {state.ProxyId} 已存在，跳过接管");
                return false;
            }

            Console.WriteLine($"[frpc-manager] 已接管隧道 {state.ProxyId}, PID={state.Pid}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[frpc-manager] 接管 PID {state.Pid} 失败: {ex.Message}");
            return false;
        }
    }

    // ─── Private ───────────────────────────────────────────

    private void WriteLog(ProxyProcess pp, LogLine line)
    {
        // 写文件
        try
        {
            var formatted = $"[{line.Timestamp:O}] {(line.IsError ? "[ERR] " : "")}{line.Text}";
            pp.LogWriter?.WriteLine(formatted);
        }
        catch { }

        // 推送事件
        try { LogEmitted?.Invoke(line); } catch { }
    }

    private static void EnsureExecutePermission(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "/bin/sh",
                Arguments = $"-c \"test -x '{path}'\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(1000);
            if (p?.ExitCode != 0)
            {
                var chmodPsi = new ProcessStartInfo
                {
                    FileName = "/bin/chmod",
                    Arguments = $"+x \"{path}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var cp = Process.Start(chmodPsi);
                cp?.WaitForExit(3000);
            }
        }
        catch { }
    }

    private static int TryGetPid(Process proc)
    {
        try { return proc.Id; }
        catch { return -1; }
    }

    private static int TryGetExitCode(Process proc)
    {
        try { return proc.ExitCode; }
        catch { return -1; }
    }

    /// <summary>
    /// 高效读取文件最后 N 行（反向扫描，避免读取整个文件）
    /// </summary>
    private static List<string> ReadLastLines(string filePath, int lineCount)
    {
        const int bufferSize = 8192;
        var lines = new List<string>();

        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (fs.Length == 0) return lines;

        var buffer = new byte[bufferSize];
        long position = fs.Length;
        var remaining = new StringBuilder();
        int foundLines = 0;

        while (position > 0 && foundLines <= lineCount)
        {
            var readSize = (int)Math.Min(bufferSize, position);
            position -= readSize;
            fs.Seek(position, SeekOrigin.Begin);
            var bytesRead = fs.Read(buffer, 0, readSize);

            var chunk = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            remaining.Insert(0, chunk);

            // 统计换行符
            for (int i = chunk.Length - 1; i >= 0; i--)
            {
                if (chunk[i] == '\n') foundLines++;
                if (foundLines > lineCount + 1) break;
            }
        }

        var allText = remaining.ToString();
        var allLines = allText.Split('\n');

        // 取最后 lineCount 行
        var startIdx = Math.Max(0, allLines.Length - lineCount);
        for (int i = startIdx; i < allLines.Length; i++)
        {
            var line = allLines[i].TrimEnd('\r');
            if (!string.IsNullOrEmpty(line))
                lines.Add(line);
        }

        return lines;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopAll();
    }
}
