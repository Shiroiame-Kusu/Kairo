using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace Kairo.Core.Services;

internal static class FrpcArchiveExtractor
{
    public static async Task<string> ExtractAsync(string archivePath, string workDir, CancellationToken ct)
    {
        var extractDir = Path.Combine(workDir, "extract");
        if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
        Directory.CreateDirectory(extractDir);

        if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(archivePath, extractDir);
        }
        else if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            await ExtractTarGzAsync(archivePath, extractDir, ct);
        }
        else
        {
            throw new InvalidOperationException("不支持的压缩格式");
        }

        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "frpc.exe" : "frpc";
        var frpcPath = Directory.GetFiles(extractDir, exeName, SearchOption.AllDirectories).FirstOrDefault()
            ?? throw new InvalidOperationException("未找到 frpc 可执行文件");

        var finalPath = Path.Combine(workDir, exeName);
        File.Copy(frpcPath, finalPath, true);
        EnsureExecutable(finalPath);

        TryCleanup(archivePath, extractDir);
        return finalPath;
    }

    private static async Task ExtractTarGzAsync(string gzFile, string extractDir, CancellationToken ct)
    {
        await using var fs = File.OpenRead(gzFile);
        await using var gzip = new GZipStream(fs, CompressionMode.Decompress);
        using var tar = new TarReader(gzip);

        TarEntry? entry;
        while ((entry = await tar.GetNextEntryAsync(cancellationToken: ct)) != null)
        {
            var fullPath = Path.Combine(extractDir, entry.Name.TrimStart('.', '/'));
            switch (entry.EntryType)
            {
                case TarEntryType.Directory:
                    Directory.CreateDirectory(fullPath);
                    break;
                case TarEntryType.RegularFile:
                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                    await using (var outFs = File.Create(fullPath))
                    {
                        if (entry.DataStream != null)
                            await entry.DataStream.CopyToAsync(outFs, ct);
                    }
                    break;
            }
        }
    }

    private static void EnsureExecutable(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "/bin/chmod",
                Arguments = $"+x \"{path}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            process?.WaitForExit(3000);
        }
        catch (System.Exception ex)
        {
            Kairo.Core.Logging.CoreLogger.Output(Kairo.Core.Logging.CoreLogLevel.Error, "Unhandled exception in Kairo.Core/Services/FrpcArchiveExtractor.cs:83", ex);
        }
    }

    private static void TryCleanup(string archivePath, string extractDir)
    {
        try { if (File.Exists(archivePath)) File.Delete(archivePath); }
        catch (System.Exception ex)
        {
            Kairo.Core.Logging.CoreLogger.Output(Kairo.Core.Logging.CoreLogLevel.Error, "Unhandled exception in Kairo.Core/Services/FrpcArchiveExtractor.cs:88", ex);
        }
        try { if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true); }
        catch (System.Exception ex)
        {
            Kairo.Core.Logging.CoreLogger.Output(Kairo.Core.Logging.CoreLogLevel.Error, "Unhandled exception in Kairo.Core/Services/FrpcArchiveExtractor.cs:89", ex);
        }
    }
}
