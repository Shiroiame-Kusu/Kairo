using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Kairo.Launcher;

class Program
{
    private static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Kairo", "bin");

    private static readonly string GuiDir = Path.Combine(AppDataDir, "gui");
    private static readonly string CliDir = Path.Combine(AppDataDir, "cli");

    static int Main(string[] args)
    {
        try
        {
            // 确定应该启动哪个程序
            bool useCli = IsLinuxHeadless() ||
                          args.Contains("--cli") ||
                          args.Contains("-c");

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
            Console.WriteLine($"[Launcher Error] {ex.Message}");
            if (ex.InnerException != null)
                Console.WriteLine($"[Inner] {ex.InnerException.Message}");
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
        Console.WriteLine($"[Launcher] 正在解压 {resourceName}...");

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

        Console.WriteLine($"[Launcher] 解压完成: {targetDir}");
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
            Console.WriteLine($"[Launcher Error] 找不到目标程序: {path}");
            Console.WriteLine("[Launcher] 提示: 请确保 Launcher 包含嵌入的压缩包资源");
            return 1;
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
            Console.WriteLine("[Launcher Error] 无法启动目标程序");
            return 1;
        }

        process.WaitForExit();
        return process.ExitCode;
    }
}
