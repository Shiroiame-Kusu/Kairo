using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Kairo.Launcher;

class Program
{
    private static readonly string AppDataDir = Path.Combine(
        GetApplicationDataPath(),
        "Kairo", "bin");

    private static readonly string GuiDir = Path.Combine(AppDataDir, "gui");
    private static readonly string CliDir = Path.Combine(AppDataDir, "cli");
    private static bool _consoleAttached;

    /// <summary>
    /// 获取应用程序数据目录（平台相关）
    /// 在 macOS 上修复可能返回 /var/root 的问题
    /// </summary>
    private static string GetApplicationDataPath()
    {
        // 在 macOS 上，明确使用用户的主目录来避免权限问题
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // 首先尝试从环境变量获取 HOME
            string? home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrWhiteSpace(home) && home != "/var/root")
            {
                return Path.Combine(home, "Library", "Application Support");
            }
            
            // 备选方案：使用 UserProfile
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(userProfile) && userProfile != "/var/root")
            {
                return Path.Combine(userProfile, "Library", "Application Support");
            }

            // 如果两种方法都失败，尝试使用标准 ApplicationData
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            // 如果还是 /var/root，则抛出异常让用户知道存在问题
            if (appData.StartsWith("/var/root", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "无法确定用户的主目录。HOME 环境变量和 UserProfile 都指向 /var/root。" +
                    "请检查应用程序权限或尝试使用 KAIRO_CONFIG_DIR 环境变量指定配置目录。");
            }
            return appData;
        }

        // 对于其他平台，使用标准的 ApplicationData
        return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    }

    static int Main(string[] args)
    {
        bool useCli = false;

        try
        {
            // 确定应该启动哪个程序
            useCli = IsLinuxHeadless() ||
                     args.Contains("--cli") ||
                     args.Contains("-c");

            if (OperatingSystem.IsWindows() && useCli)
            {
                EnsureConsole();
            }

            string targetDir = useCli ? CliDir : GuiDir;
            string hashesResourceName = useCli ? "kairo-cli.hashes" : "kairo-gui.hashes";
            string archiveResourceName = useCli ? "kairo-cli.tar.gz" : "kairo-gui.tar.gz";
            string exeName = useCli
                ? (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "kairo-cli.exe" : "kairo-cli")
                : (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Kairo.exe" : "Kairo");

            string targetPath = Path.Combine(targetDir, exeName);

            // 检查是否需要解压（基于文件哈希校验）
            if (!VerifyHashes(targetDir, hashesResourceName) || !File.Exists(targetPath))
            {
                ExtractEmbeddedArchive(archiveResourceName, targetDir);
                SaveEmbeddedHashes(targetDir, hashesResourceName);
            }

            // 过滤掉 --cli/-c 参数
            var filteredArgs = args.Where(a => a != "--cli" && a != "-c").ToArray();

            // 启动目标程序
            return LaunchProcess(targetPath, filteredArgs);
        }
        catch (Exception ex)
        {
            ReportLauncherError(ex, useCli);
            return 1;
        }
    }

    /// <summary>
    /// 检测是否为 Linux 无头模式（无桌面环境）
    /// </summary>
    private static bool IsLinuxHeadless()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return false;

        // 检查 DISPLAY 和 WAYLAND_DISPLAY 环境变量
        var display = Environment.GetEnvironmentVariable("DISPLAY");
        var waylandDisplay = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");

        // 两者都为空则认为是无头环境
        return string.IsNullOrEmpty(display) && string.IsNullOrEmpty(waylandDisplay);
    }

    /// <summary>
    /// 验证目录中的文件哈希是否与嵌入的哈希文件匹配
    /// </summary>
    private static bool VerifyHashes(string targetDir, string hashesResourceName)
    {
        try
        {
            if (!Directory.Exists(targetDir)) return false;

            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream(hashesResourceName);
            if (stream == null) return false;

            using var reader = new StreamReader(stream);
            var expectedHashes = ParseHashesFile(reader.ReadToEnd());

            if (expectedHashes.Count == 0) return false;

            foreach (var (relativePath, expectedHash) in expectedHashes)
            {
                var fullPath = Path.Combine(targetDir, relativePath);
                if (!File.Exists(fullPath)) return false;

                var actualHash = ComputeFileMd5(fullPath);
                if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// 保存嵌入的哈希文件到目标目录
    /// </summary>
    private static void SaveEmbeddedHashes(string targetDir, string hashesResourceName)
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream(hashesResourceName);
            if (stream == null) return;

            using var reader = new StreamReader(stream);
            var content = reader.ReadToEnd();
            File.WriteAllText(Path.Combine(targetDir, ".hashes"), content);
        }
        catch { }
    }

    /// <summary>
    /// 解析哈希文件内容
    /// </summary>
    private static Dictionary<string, string> ParseHashesFile(string content)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Trim().Split("  ", 2); // MD5SUM 格式: "hash  filename"
            if (parts.Length == 2)
            {
                var hash = parts[0].Trim();
                var file = parts[1].Trim().TrimStart('.', '/', '\\');
                if (!string.IsNullOrEmpty(file))
                    result[file] = hash;
            }
        }
        return result;
    }

    /// <summary>
    /// 计算文件的 MD5 哈希
    /// </summary>
    private static string ComputeFileMd5(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = MD5.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void ExtractEmbeddedArchive(string resourceName, string targetDir)
    {
        LogInfo($"[Launcher] 正在解压 {resourceName}...");

        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName);

        if (stream == null)
        {
            throw new Exception($"未找到嵌入资源: {resourceName}\n" +
                "请确保使用 build-launcher.sh 脚本编译 Launcher");
        }

        // 清理并创建目标目录
        if (Directory.Exists(targetDir))
        {
            try { Directory.Delete(targetDir, true); }
            catch { }
        }
        Directory.CreateDirectory(targetDir);

        // 解压 tar.gz
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var tar = new TarReader(gzip);

        TarEntry? entry;
        while ((entry = tar.GetNextEntry()) != null)
        {
            var entryPath = entry.Name.TrimStart('.', '/', '\\');
            if (string.IsNullOrEmpty(entryPath)) continue;

            var fullPath = Path.Combine(targetDir, entryPath);

            switch (entry.EntryType)
            {
                case TarEntryType.Directory:
                    Directory.CreateDirectory(fullPath);
                    break;

                case TarEntryType.RegularFile:
                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                    using (var fileStream = File.Create(fullPath))
                    {
                        entry.DataStream?.CopyTo(fileStream);
                    }
                    break;
            }
        }

        // 设置执行权限 (Linux/macOS)
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            SetExecutablePermissions(targetDir);
        }

        LogInfo($"[Launcher] 解压完成: {targetDir}");
    }

    private static void SetExecutablePermissions(string dir)
    {
        // 设置所有可执行文件和 .so 文件的权限
        var extensions = new[] { "", ".so" };
        
        foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
        {
            var ext = Path.GetExtension(file);
            var hasNoExt = string.IsNullOrEmpty(ext);
            var isSo = ext.Equals(".so", StringComparison.OrdinalIgnoreCase) ||
                       file.Contains(".so.");

            if (hasNoExt || isSo)
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "/bin/chmod",
                        Arguments = $"+x \"{file}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var process = Process.Start(psi);
                    process?.WaitForExit(1000);
                }
                catch { }
            }
        }
    }

    private static int LaunchProcess(string path, string[] args)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"找不到目标程序: {path}\n请确保 Launcher 包含嵌入的压缩包资源。",
                path);
        }

        var psi = new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = false,
            CreateNoWindow = false,
            WorkingDirectory = Path.GetDirectoryName(path)
        };

        // 设置 LD_LIBRARY_PATH 以便找到 native libraries
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var libPath = Path.GetDirectoryName(path);
            var existingPath = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH") ?? "";
            psi.Environment["LD_LIBRARY_PATH"] = string.IsNullOrEmpty(existingPath)
                ? libPath
                : $"{libPath}:{existingPath}";
        }

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi);
        if (process == null)
        {
            throw new InvalidOperationException("无法启动目标程序");
        }

        process.WaitForExit();
        return process.ExitCode;
    }

    private static void LogInfo(string message)
    {
        if (!CanWriteToConsole())
            return;

        try
        {
            Console.WriteLine(message);
        }
        catch { }
    }

    private static void LogError(string message)
    {
        if (!CanWriteToConsole())
            return;

        try
        {
            Console.Error.WriteLine(message);
        }
        catch { }
    }

    private static bool CanWriteToConsole()
    {
        return !OperatingSystem.IsWindows() || _consoleAttached;
    }

    private static void ReportLauncherError(Exception ex, bool useCli)
    {
        var builder = new StringBuilder()
            .Append("[Launcher Error] ")
            .Append(ex.Message);

        if (ex.InnerException != null)
        {
            builder.AppendLine()
                .Append("[Inner] ")
                .Append(ex.InnerException.Message);
        }

        var errorMessage = builder.ToString();
        LogError(errorMessage);

        if (OperatingSystem.IsWindows() && !(useCli && _consoleAttached))
        {
            ShowWindowsErrorDialog(errorMessage);
        }
    }

    private static void EnsureConsole()
    {
        if (!OperatingSystem.IsWindows() || _consoleAttached)
            return;

        if (AttachConsole(ATTACH_PARENT_PROCESS) || AllocConsole())
        {
            _consoleAttached = true;
            ResetConsoleStreams();
        }
    }

    private static void ResetConsoleStreams()
    {
        try
        {
            var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
            var stderr = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
            Console.SetOut(stdout);
            Console.SetError(stderr);
        }
        catch { }
    }

    private static void ShowWindowsErrorDialog(string message)
    {
        try
        {
            MessageBoxW(IntPtr.Zero, message, "Kairo Launcher", 0x00000010);
        }
        catch { }
    }

    private const int ATTACH_PARENT_PROCESS = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
