using System.Runtime.CompilerServices;

namespace Kairo.Cli.Utils;

/// <summary>
/// 日志级别
/// </summary>
public enum LogLevel
{
    Debug = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
    None = 99
}

/// <summary>
/// CLI 日志记录器
/// </summary>
public static class Logger
{
    private static LogLevel _minLevel = LogLevel.Info;
    private static bool _writeToFile;
    private static string? _logFilePath;
    private static readonly object _lock = new();
    
    /// <summary>
    /// 当前最小日志级别
    /// </summary>
    public static LogLevel MinLevel => _minLevel;
    
    /// <summary>
    /// 是否启用调试模式
    /// </summary>
    public static bool IsDebugEnabled => _minLevel <= LogLevel.Debug;

    /// <summary>
    /// 初始化日志系统
    /// </summary>
    /// <param name="minLevel">最小日志级别</param>
    /// <param name="logToFile">是否写入文件</param>
    /// <param name="logFilePath">日志文件路径（可选）</param>
    public static void Initialize(LogLevel minLevel = LogLevel.Info, bool logToFile = false, string? logFilePath = null)
    {
        _minLevel = minLevel;
        _writeToFile = logToFile;
        
        if (logToFile)
        {
            _logFilePath = logFilePath ?? GetDefaultLogPath();
            try
            {
                var dir = Path.GetDirectoryName(_logFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                
                // 写入日志头
                File.AppendAllText(_logFilePath, $"\n{new string('=', 60)}\n");
                File.AppendAllText(_logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Kairo CLI 日志开始\n");
                File.AppendAllText(_logFilePath, $"{new string('=', 60)}\n\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[警告] 无法创建日志文件: {ex.Message}");
                _writeToFile = false;
            }
        }
        
        Debug($"日志系统已初始化 - 级别: {minLevel}, 文件记录: {logToFile}");
    }

    private static string GetDefaultLogPath()
    {
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Kairo", "logs", "cli");
        return Path.Combine(logDir, $"cli-{DateTime.Now:yyyyMMdd-HHmmss}.log");
    }

    /// <summary>
    /// 记录调试信息
    /// </summary>
    public static void Debug(string message,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0)
    {
        Log(LogLevel.Debug, message, memberName, sourceFilePath, sourceLineNumber);
    }

    /// <summary>
    /// 记录信息
    /// </summary>
    public static void Info(string message,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0)
    {
        Log(LogLevel.Info, message, memberName, sourceFilePath, sourceLineNumber);
    }

    /// <summary>
    /// 记录警告
    /// </summary>
    public static void Warning(string message,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0)
    {
        Log(LogLevel.Warning, message, memberName, sourceFilePath, sourceLineNumber);
    }

    /// <summary>
    /// 记录错误
    /// </summary>
    public static void Error(string message,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0)
    {
        Log(LogLevel.Error, message, memberName, sourceFilePath, sourceLineNumber);
    }

    /// <summary>
    /// 记录异常
    /// </summary>
    public static void Exception(Exception ex, string? context = null,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0)
    {
        var message = context != null 
            ? $"{context}: {ex.GetType().Name}: {ex.Message}"
            : $"{ex.GetType().Name}: {ex.Message}";
        
        Log(LogLevel.Error, message, memberName, sourceFilePath, sourceLineNumber);
        
        // 调试模式下输出堆栈
        if (_minLevel <= LogLevel.Debug && ex.StackTrace != null)
        {
            Log(LogLevel.Debug, $"堆栈跟踪:\n{ex.StackTrace}", memberName, sourceFilePath, sourceLineNumber);
        }
    }

    /// <summary>
    /// 记录HTTP请求
    /// </summary>
    public static void HttpRequest(string method, string url,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0)
    {
        Debug($"HTTP {method} -> {url}", memberName, sourceFilePath, sourceLineNumber);
    }

    /// <summary>
    /// 记录HTTP响应
    /// </summary>
    public static void HttpResponse(string method, string url, int statusCode, long? durationMs = null,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0)
    {
        var durationStr = durationMs.HasValue ? $" ({durationMs}ms)" : "";
        Debug($"HTTP {method} <- {statusCode} {url}{durationStr}", memberName, sourceFilePath, sourceLineNumber);
    }

    /// <summary>
    /// 记录进程启动
    /// </summary>
    public static void ProcessStart(string executable, string? arguments = null,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0)
    {
        var argStr = string.IsNullOrEmpty(arguments) ? "" : $" {arguments}";
        Debug($"启动进程: {executable}{argStr}", memberName, sourceFilePath, sourceLineNumber);
    }

    /// <summary>
    /// 记录进程结束
    /// </summary>
    public static void ProcessExit(int pid, int exitCode,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0)
    {
        Debug($"进程退出: PID={pid}, ExitCode={exitCode}", memberName, sourceFilePath, sourceLineNumber);
    }

    /// <summary>
    /// 记录配置操作
    /// </summary>
    public static void Config(string operation, string? details = null,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0)
    {
        var detailStr = string.IsNullOrEmpty(details) ? "" : $" - {details}";
        Debug($"配置{operation}{detailStr}", memberName, sourceFilePath, sourceLineNumber);
    }

    /// <summary>
    /// 记录文件操作
    /// </summary>
    public static void FileOperation(string operation, string path,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0)
    {
        Debug($"文件{operation}: {path}", memberName, sourceFilePath, sourceLineNumber);
    }

    /// <summary>
    /// 记录方法进入
    /// </summary>
    public static void MethodEntry(string? details = null,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0)
    {
        var detailStr = string.IsNullOrEmpty(details) ? "" : $" ({details})";
        Debug($">>> 进入 {memberName}{detailStr}", memberName, sourceFilePath, sourceLineNumber);
    }

    /// <summary>
    /// 记录方法退出
    /// </summary>
    public static void MethodExit(string? result = null,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0)
    {
        var resultStr = string.IsNullOrEmpty(result) ? "" : $" => {result}";
        Debug($"<<< 退出 {memberName}{resultStr}", memberName, sourceFilePath, sourceLineNumber);
    }

    private static void Log(LogLevel level, string message, string memberName, string sourceFilePath, int sourceLineNumber)
    {
        if (level < _minLevel) return;

        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        var fileName = Path.GetFileNameWithoutExtension(sourceFilePath);
        var levelStr = GetLevelString(level);
        var color = GetLevelColor(level);

        string formattedMessage;
        if (_minLevel <= LogLevel.Debug)
        {
            // 调试模式显示更多信息
            formattedMessage = $"[{timestamp}] [{levelStr}] [{fileName}.{memberName}:{sourceLineNumber}] {message}";
        }
        else
        {
            formattedMessage = $"[{levelStr}] {message}";
        }

        lock (_lock)
        {
            // 控制台输出
            var prevColor = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(formattedMessage);
            Console.ForegroundColor = prevColor;

            // 文件输出
            if (_writeToFile && _logFilePath != null)
            {
                try
                {
                    var fileMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{levelStr}] [{fileName}.{memberName}:{sourceLineNumber}] {message}";
                    File.AppendAllText(_logFilePath, fileMessage + Environment.NewLine);
                }
                catch { /* 忽略文件写入错误 */ }
            }
        }
    }

    private static string GetLevelString(LogLevel level) => level switch
    {
        LogLevel.Debug => "调试",
        LogLevel.Info => "信息",
        LogLevel.Warning => "警告",
        LogLevel.Error => "错误",
        _ => "未知"
    };

    private static ConsoleColor GetLevelColor(LogLevel level) => level switch
    {
        LogLevel.Debug => ConsoleColor.Gray,
        LogLevel.Info => ConsoleColor.White,
        LogLevel.Warning => ConsoleColor.Yellow,
        LogLevel.Error => ConsoleColor.Red,
        _ => ConsoleColor.White
    };
}
