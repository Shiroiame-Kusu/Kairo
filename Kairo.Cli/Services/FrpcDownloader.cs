using System.IO.Compression;
using System.Formats.Tar;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Diagnostics;
using Kairo.Core;
using Kairo.Cli.Configuration;

namespace Kairo.Cli.Services;

/// <summary>
/// frpc 下载服务
/// </summary>
public class FrpcDownloader
{
    private readonly HttpClient _http = new();
    private const int MaxAttempts = 3;

    public class DownloadResult
    {
        public bool Success { get; set; }
        public string? FrpcPath { get; set; }
        public string? Message { get; set; }
    }

    public async Task<DownloadResult> DownloadAsync(CancellationToken token = default)
    {
        try
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Kairo-FrpcDownloader-CLI");

            Console.WriteLine("[下载] 正在获取最新版本信息...");

            string apiMirror = "https://api-gh.1l1.icu/repos/LoCyan-Team/LocyanFrpPureApp/releases/latest";
            string apiOrigin = "https://api.github.com/repos/LoCyan-Team/LocyanFrpPureApp/releases/latest";

            JsonObject? release = await TryFetchAsync(apiMirror, token) ?? 
                                  await TryFetchAsync(apiOrigin, token);
            
            if (release == null)
                return new DownloadResult { Success = false, Message = "无法获取版本信息" };

            var (version, assets, asset, assetName, platform, arch) = SelectBestAsset(release);
            Console.WriteLine($"[下载] 最新版本: {version} ({platform}-{arch})");

            string downloadUrl = GetNodeString(asset["browser_download_url"]);
            if (string.IsNullOrWhiteSpace(downloadUrl))
                return new DownloadResult { Success = false, Message = "下载地址缺失" };

            // 使用镜像源
            if (CliConfigManager.Config.UsingDownloadMirror)
            {
                string releaseName = GetNodeString(release["name"]);
                if (string.IsNullOrWhiteSpace(releaseName))
                    releaseName = GetNodeString(release["tag_name"]);
                if (string.IsNullOrWhiteSpace(releaseName))
                    releaseName = version;
                downloadUrl = "https://mirrors.locyan.cn/github-release/LoCyan-Team/LoCyanFrpPureApp/" +
                              Uri.EscapeDataString(releaseName) + "/" + Uri.EscapeDataString(assetName);
            }

            Console.WriteLine($"[下载] 下载地址: {downloadUrl}");

            // 准备下载目录
            string workDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
                "Kairo", "frpc");
            Directory.CreateDirectory(workDir);

            string tempFile = Path.Combine(workDir, assetName);
            if (File.Exists(tempFile)) File.Delete(tempFile);

            // 带重试的下载
            Exception? lastError = null;
            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                if (token.IsCancellationRequested)
                    return new DownloadResult { Success = false, Message = "下载已取消" };

                try
                {
                    if (attempt > 1)
                        Console.WriteLine($"[下载] 重试 ({attempt}/{MaxAttempts})...");

                    await DownloadFileAsync(downloadUrl, tempFile, token);
                    
                    Console.WriteLine("[下载] 正在校验文件...");
                    string releaseName = GetNodeString(release["name"]);
                    if (string.IsNullOrWhiteSpace(releaseName))
                        releaseName = version;
                    await VerifyChecksumAsync(tempFile, assets, assetName, token, releaseName);

                    Console.WriteLine("[下载] 正在解压...");
                    string frpcPath = await ExtractAsync(tempFile, workDir);

                    // 保存配置
                    CliConfigManager.Config.FrpcPath = frpcPath;
                    CliConfigManager.Save();

                    return new DownloadResult { Success = true, FrpcPath = frpcPath };
                }
                catch (OperationCanceledException)
                {
                    return new DownloadResult { Success = false, Message = "下载已取消" };
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    Console.WriteLine($"[下载] 失败: {ex.Message}");
                    if (attempt < MaxAttempts)
                        await Task.Delay(1500, token);
                }
            }

            return new DownloadResult { Success = false, Message = lastError?.Message ?? "下载失败" };
        }
        catch (Exception ex)
        {
            return new DownloadResult { Success = false, Message = ex.Message };
        }
    }

    private async Task<JsonObject?> TryFetchAsync(string url, CancellationToken token)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            using var response = await _http.GetAsync(url, cts.Token);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cts.Token);
            return JsonNode.Parse(content) as JsonObject;
        }
        catch { return null; }
    }

    private (string version, JsonArray assets, JsonObject asset, string assetName, string platform, string arch)
        SelectBestAsset(JsonObject release)
    {
        var tag = GetNodeString(release["tag_name"]);
        var m = Regex.Match(tag, "v(\\d+\\.\\d+\\.\\d+)-\\d+");
        string version = m.Success ? m.Groups[1].Value : tag.TrimStart('v');
        var assets = release["assets"] as JsonArray ?? new JsonArray();

        string arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X86 => "386",
            Architecture.Arm => "arm",
            Architecture.Arm64 => "arm64",
            _ => "amd64"
        };

        string platform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" :
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" :
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "darwin" : "linux";

        string basePattern = $"frp_LoCyanFrp-{version}_{platform}_{arch}";
        var assetObjects = assets.OfType<JsonObject>().ToList();
        var candidates = assetObjects
            .Where(a => GetAssetName(a).StartsWith(basePattern, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (candidates.Count == 0)
            candidates = assetObjects.Where(a => GetAssetName(a).Contains($"{platform}_{arch}")).ToList();

        if (candidates.Count == 0)
        {
            var any = assetObjects.FirstOrDefault() ?? throw new Exception("未找到可用资产");
            return (version, assets, any, GetAssetName(any), platform, arch);
        }

        JsonObject? pick = null;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            pick = candidates.FirstOrDefault(a => GetAssetName(a).EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        else
            pick = candidates.FirstOrDefault(a => GetAssetName(a).EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
                   ?? candidates.FirstOrDefault(a => GetAssetName(a).EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

        pick ??= candidates.First();
        return (version, assets, pick, GetAssetName(pick), platform, arch);
    }

    private async Task DownloadFileAsync(string url, string destPath, CancellationToken token)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        var receivedBytes = 0L;
        var lastProgressUpdate = DateTime.UtcNow;

        await using var contentStream = await response.Content.ReadAsStreamAsync(token);
        await using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

        var buffer = new byte[8192];
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, token)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), token);
            receivedBytes += bytesRead;

            if ((DateTime.UtcNow - lastProgressUpdate).TotalMilliseconds > 500)
            {
                lastProgressUpdate = DateTime.UtcNow;
                if (totalBytes > 0)
                {
                    var percent = (double)receivedBytes / totalBytes * 100;
                    Console.Write($"\r[下载] 进度: {FormatBytes(receivedBytes)} / {FormatBytes(totalBytes)} ({percent:F1}%)    ");
                }
                else
                {
                    Console.Write($"\r[下载] 已下载: {FormatBytes(receivedBytes)}    ");
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine($"[下载] 下载完成: {FormatBytes(receivedBytes)}");
    }

    private async Task VerifyChecksumAsync(string filePath, JsonArray assets, string assetName, 
        CancellationToken token, string releaseName)
    {
        string assetPrefix = assetName.Split('.')[0];
        var assetObjects = assets.OfType<JsonObject>().ToList();

        JsonObject? checksumAsset = assetObjects.FirstOrDefault(a =>
                GetAssetName(a).EndsWith(".sha256", StringComparison.OrdinalIgnoreCase) &&
                GetAssetName(a).Contains(assetPrefix))
            ?? assetObjects.FirstOrDefault(a =>
                GetAssetName(a).EndsWith(".md5", StringComparison.OrdinalIgnoreCase) &&
                GetAssetName(a).Contains(assetPrefix));

        if (checksumAsset == null)
        {
            Console.WriteLine("[校验] 未提供校验文件，跳过校验");
            return;
        }

        string checksumUrl = GetNodeString(checksumAsset["browser_download_url"]);
        if (string.IsNullOrWhiteSpace(checksumUrl))
        {
            Console.WriteLine("[校验] 校验文件链接缺失，跳过校验");
            return;
        }

        if (CliConfigManager.Config.UsingDownloadMirror)
        {
            string checksumFileName = GetAssetName(checksumAsset);
            checksumUrl = "https://mirrors.locyan.cn/github-release/LoCyan-Team/LoCyanFrpPureApp/" +
                          Uri.EscapeDataString(releaseName) + "/" + Uri.EscapeDataString(checksumFileName);
        }

        var checksumContent = await _http.GetStringAsync(checksumUrl, token);
        string? expectedHash = null;
        bool sha256 = false;

        foreach (var line in checksumContent.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (Regex.IsMatch(parts[0], "^[a-fA-F0-9]{64}$"))
            {
                expectedHash = parts[0].ToLowerInvariant();
                sha256 = true;
                break;
            }
            if (Regex.IsMatch(parts[0], "^[a-fA-F0-9]{32}$"))
            {
                expectedHash = parts[0].ToLowerInvariant();
                break;
            }
        }

        if (expectedHash == null)
        {
            Console.WriteLine("[校验] 校验文件无有效哈希，跳过校验");
            return;
        }

        string actualHash;
        await using (var fs = File.OpenRead(filePath))
        {
            actualHash = sha256 
                ? Convert.ToHexString(await SHA256.HashDataAsync(fs, token)).ToLowerInvariant()
                : Convert.ToHexString(await MD5.HashDataAsync(fs, token)).ToLowerInvariant();
        }

        if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
            throw new Exception("文件哈希不匹配");

        Console.WriteLine("[校验] 校验通过");
    }

    private async Task<string> ExtractAsync(string archivePath, string workDir)
    {
        string extractDir = Path.Combine(workDir, "extract");
        if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
        Directory.CreateDirectory(extractDir);

        if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            ZipFile.ExtractToDirectory(archivePath, extractDir);
        else if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            await ExtractTarGzAsync(archivePath, extractDir);
        else
            throw new Exception("不支持的压缩格式");

        string exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "frpc.exe" : "frpc";
        var frpcPath = Directory.GetFiles(extractDir, exeName, SearchOption.AllDirectories).FirstOrDefault()
            ?? throw new Exception("未找到 frpc 可执行文件");

        string finalPath = Path.Combine(workDir, exeName);
        File.Copy(frpcPath, finalPath, true);

        // 设置执行权限
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "/bin/chmod",
                    Arguments = $"+x \"{finalPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(psi);
                process?.WaitForExit(3000);
            }
            catch { }
        }

        // 清理
        try
        {
            File.Delete(archivePath);
            Directory.Delete(extractDir, true);
        }
        catch { }

        return finalPath;
    }

    private static async Task ExtractTarGzAsync(string gzFile, string extractDir)
    {
        await using var fs = File.OpenRead(gzFile);
        await using var gzip = new GZipStream(fs, CompressionMode.Decompress);
        using var tar = new TarReader(gzip);

        TarEntry? entry;
        while ((entry = await tar.GetNextEntryAsync()) != null)
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
                            await entry.DataStream.CopyToAsync(outFs);
                    }
                    break;
            }
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return bytes + " B";
        double kb = bytes / 1024d;
        if (kb < 1024) return kb.ToString("F1") + " KB";
        double mb = kb / 1024d;
        return mb < 1024 ? mb.ToString("F2") + " MB" : (mb / 1024d).ToString("F2") + " GB";
    }

    private static string GetNodeString(JsonNode? node) =>
        node is System.Text.Json.Nodes.JsonValue value && value.TryGetValue<string>(out var str) 
            ? str : node?.ToString() ?? "";

    private static string GetAssetName(JsonObject? obj) =>
        obj == null ? "" : GetNodeString(obj["name"]);
}
