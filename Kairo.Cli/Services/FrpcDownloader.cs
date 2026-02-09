using System.IO.Compression;
using System.Formats.Tar;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Diagnostics;
using Kairo.Core;
using Kairo.Cli.Configuration;
using Kairo.Cli.Utils;

namespace Kairo.Cli.Services;

/// <summary>
/// frpc 下载服务
/// </summary>
public class FrpcDownloader : IDisposable
{
    private readonly HttpClient _http = new();
    private const int MaxAttempts = 3;

    public class DownloadResult
    {
        public bool Success { get; set; }
        public string? FrpcPath { get; set; }
        public string? Message { get; set; }
    }

    public void Dispose()
    {
        Logger.Debug("FrpcDownloader Dispose 调用");
        _http.Dispose();
    }

    /// <summary>
    /// 是否强制使用 GitHub 源（通过命令行参数设置）
    /// </summary>
    public bool ForceGitHub { get; set; } = false;

    public async Task<DownloadResult> DownloadAsync(CancellationToken token = default)
    {
        Logger.MethodEntry();
        var overallSw = Stopwatch.StartNew();
        
        try
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Kairo-FrpcDownloader-CLI");
            Logger.Debug("设置 User-Agent: Kairo-FrpcDownloader-CLI");

            Console.WriteLine("[下载] 正在获取最新版本信息...");
            Logger.Info("开始获取最新版本信息");
            
            if (ForceGitHub)
            {
                Logger.Info("用户强制使用 GitHub 源");
                Console.WriteLine("[下载] 使用 GitHub 源（用户指定）");
            }

            string apiMirror = "https://api-gh.1l1.icu/repos/LoCyan-Team/LocyanFrpPureApp/releases/latest";
            string apiOrigin = "https://api.github.com/repos/LoCyan-Team/LocyanFrpPureApp/releases/latest";

            Logger.Debug($"尝试镜像 API: {apiMirror}");
            JsonObject? release = await TryFetchAsync(apiMirror, token);
            if (release == null)
            {
                Logger.Debug("镜像 API 失败，尝试原始 API");
                Logger.Debug($"尝试原始 API: {apiOrigin}");
                release = await TryFetchAsync(apiOrigin, token);
            }
            
            if (release == null)
            {
                Logger.Error("无法获取版本信息（两个 API 都失败）");
                Logger.MethodExit("失败");
                return new DownloadResult { Success = false, Message = "无法获取版本信息" };
            }
            
            Logger.Info("成功获取版本信息");

            var (version, assets, asset, assetName, platform, arch) = SelectBestAsset(release);
            Logger.Info($"选择资产: 版本={version}, 平台={platform}, 架构={arch}, 文件={assetName}");
            Console.WriteLine($"[下载] 最新版本: {version} ({platform}-{arch})");;

            string githubDownloadUrl = GetNodeString(asset["browser_download_url"]);
            if (string.IsNullOrWhiteSpace(githubDownloadUrl))
            {
                Logger.Error("下载地址缺失");
                Logger.MethodExit("失败");
                return new DownloadResult { Success = false, Message = "下载地址缺失" };
            }
            Logger.Debug($"GitHub 下载地址: {githubDownloadUrl}");

            // 构建镜像下载地址
            string releaseName = GetNodeString(release["name"]);
            if (string.IsNullOrWhiteSpace(releaseName))
                releaseName = GetNodeString(release["tag_name"]);
            if (string.IsNullOrWhiteSpace(releaseName))
                releaseName = version;
            string mirrorDownloadUrl = "https://mirrors.locyan.cn/github-release/LoCyan-Team/LoCyanFrpPureApp/" +
                          Uri.EscapeDataString(releaseName) + "/" + Uri.EscapeDataString(assetName);
            Logger.Debug($"镜像下载地址: {mirrorDownloadUrl}");

            // 确定是否使用镜像（如果强制 GitHub 则不使用镜像）
            bool useMirror = CliConfigManager.Config.UsingDownloadMirror && !ForceGitHub;
            Logger.Info($"下载源选择: {(useMirror ? "镜像" : "GitHub")}");

            // 准备下载目录
            string workDir = Path.Combine(
                EnvironmentDetector.GetApplicationDataPath(), 
                "Kairo", "frpc");
            Logger.FileOperation("创建目录", workDir);
            Directory.CreateDirectory(workDir);

            string tempFile = Path.Combine(workDir, assetName);
            Logger.Debug($"临时文件路径: {tempFile}");
            if (File.Exists(tempFile))
            {
                Logger.Debug("删除已存在的临时文件");
                File.Delete(tempFile);
            }

            // 带重试和 fallback 的下载
            Exception? lastError = null;
            string currentUrl = useMirror ? mirrorDownloadUrl : githubDownloadUrl;
            string currentSource = useMirror ? "镜像" : "GitHub";
            bool canFallback = useMirror; // 只有使用镜像时才能 fallback 到 GitHub
            
            Console.WriteLine($"[下载] 使用{currentSource}下载: {currentUrl}");
            Logger.Debug($"开始下载，最大重试次数: {MaxAttempts}");
            
            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                Logger.Debug($"[{currentSource}] 下载尝试 {attempt}/{MaxAttempts}");
                
                if (token.IsCancellationRequested)
                {
                    Logger.Warning("下载被用户取消");
                    Logger.MethodExit("取消");
                    return new DownloadResult { Success = false, Message = "下载已取消" };
                }

                try
                {
                    if (attempt > 1)
                    {
                        Logger.Info($"[{currentSource}] 重试下载 ({attempt}/{MaxAttempts})");
                        Console.WriteLine($"[下载] [{currentSource}] 重试 ({attempt}/{MaxAttempts})...");
                    }

                    var downloadSw = Stopwatch.StartNew();
                    await DownloadFileAsync(currentUrl, tempFile, token);
                    downloadSw.Stop();
                    Logger.Info($"文件下载完成，耗时: {downloadSw.ElapsedMilliseconds}ms");
                    
                    Console.WriteLine("[下载] 正在校验文件...");
                    Logger.Debug("开始文件校验");
                    await VerifyChecksumAsync(tempFile, assets, assetName, token, releaseName);
                    Logger.Info("文件校验通过");

                    Console.WriteLine("[下载] 正在解压...");
                    Logger.Debug("开始解压文件");
                    string frpcPath = await ExtractAsync(tempFile, workDir);
                    Logger.Info($"解压完成，frpc 路径: {frpcPath}");

                    // 保存配置
                    Logger.Config("保存", $"FrpcPath={frpcPath}");
                    CliConfigManager.Config.FrpcPath = frpcPath;
                    CliConfigManager.Save();

                    overallSw.Stop();
                    Logger.Info($"下载流程完成，总耗时: {overallSw.ElapsedMilliseconds}ms");
                    Logger.MethodExit("成功");
                    return new DownloadResult { Success = true, FrpcPath = frpcPath };
                }
                catch (OperationCanceledException)
                {
                    Logger.Warning("下载被取消");
                    Logger.MethodExit("取消");
                    return new DownloadResult { Success = false, Message = "下载已取消" };
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    Logger.Exception(ex, $"[{currentSource}] 下载尝试 {attempt} 失败");
                    Console.WriteLine($"[下载] 失败: {ex.Message}");
                    if (attempt < MaxAttempts)
                    {
                        Logger.Debug("等待 1.5 秒后重试");
                        await Task.Delay(1500, token);
                    }
                }
            }

            // Fallback：如果镜像失败了，尝试 GitHub
            if (canFallback)
            {
                Logger.Warning($"[{currentSource}] 下载失败 {MaxAttempts} 次，切换到 GitHub 源");
                Console.WriteLine($"[下载] 镜像下载失败 {MaxAttempts} 次，正在切换到 GitHub 源...");
                
                currentUrl = githubDownloadUrl;
                currentSource = "GitHub";
                canFallback = false; // 防止无限循环
                
                // 清理临时文件
                if (File.Exists(tempFile))
                {
                    Logger.Debug("清理失败的临时文件");
                    try { File.Delete(tempFile); } catch { }
                }
                
                Console.WriteLine($"[下载] 使用 GitHub 下载: {currentUrl}");
                
                for (int attempt = 1; attempt <= MaxAttempts; attempt++)
                {
                    Logger.Debug($"[{currentSource}] 下载尝试 {attempt}/{MaxAttempts}");
                    
                    if (token.IsCancellationRequested)
                    {
                        Logger.Warning("下载被用户取消");
                        Logger.MethodExit("取消");
                        return new DownloadResult { Success = false, Message = "下载已取消" };
                    }

                    try
                    {
                        if (attempt > 1)
                        {
                            Logger.Info($"[{currentSource}] 重试下载 ({attempt}/{MaxAttempts})");
                            Console.WriteLine($"[下载] [GitHub] 重试 ({attempt}/{MaxAttempts})...");
                        }

                        var downloadSw = Stopwatch.StartNew();
                        await DownloadFileAsync(currentUrl, tempFile, token);
                        downloadSw.Stop();
                        Logger.Info($"文件下载完成，耗时: {downloadSw.ElapsedMilliseconds}ms");
                        
                        Console.WriteLine("[下载] 正在校验文件...");
                        Logger.Debug("开始文件校验");
                        await VerifyChecksumAsync(tempFile, assets, assetName, token, releaseName);
                        Logger.Info("文件校验通过");

                        Console.WriteLine("[下载] 正在解压...");
                        Logger.Debug("开始解压文件");
                        string frpcPath = await ExtractAsync(tempFile, workDir);
                        Logger.Info($"解压完成，frpc 路径: {frpcPath}");

                        // 保存配置
                        Logger.Config("保存", $"FrpcPath={frpcPath}");
                        CliConfigManager.Config.FrpcPath = frpcPath;
                        CliConfigManager.Save();

                        overallSw.Stop();
                        Logger.Info($"下载流程完成（已 fallback 到 GitHub），总耗时: {overallSw.ElapsedMilliseconds}ms");
                        Logger.MethodExit("成功");
                        return new DownloadResult { Success = true, FrpcPath = frpcPath };
                    }
                    catch (OperationCanceledException)
                    {
                        Logger.Warning("下载被取消");
                        Logger.MethodExit("取消");
                        return new DownloadResult { Success = false, Message = "下载已取消" };
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                        Logger.Exception(ex, $"[GitHub] 下载尝试 {attempt} 失败");
                        Console.WriteLine($"[下载] 失败: {ex.Message}");
                        if (attempt < MaxAttempts)
                        {
                            Logger.Debug("等待 1.5 秒后重试");
                            await Task.Delay(1500, token);
                        }
                    }
                }
            }

            Logger.Error($"下载失败（所有源都失败）: {lastError?.Message}");
            Logger.MethodExit("失败");
            return new DownloadResult { Success = false, Message = lastError?.Message ?? "下载失败（镜像和 GitHub 都已尝试）" };
        }
        catch (Exception ex)
        {
            Logger.Exception(ex, "DownloadAsync 发生未预期异常");
            Logger.MethodExit("异常");
            return new DownloadResult { Success = false, Message = ex.Message };
        }
    }

    private async Task<JsonObject?> TryFetchAsync(string url, CancellationToken token)
    {
        Logger.Debug($"TryFetchAsync: {url}");
        var sw = Stopwatch.StartNew();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(TimeSpan.FromSeconds(10));
            Logger.Debug("设置超时: 10秒");

            Logger.HttpRequest("GET", url);
            using var response = await _http.GetAsync(url, cts.Token);
            sw.Stop();
            Logger.HttpResponse("GET", url, (int)response.StatusCode, sw.ElapsedMilliseconds);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cts.Token);
            Logger.Debug($"响应内容长度: {content.Length} 字节");
            
            var result = JsonNode.Parse(content) as JsonObject;
            Logger.Debug($"解析结果: {(result != null ? "成功" : "失败")}");
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            Logger.Warning($"TryFetchAsync 失败 ({sw.ElapsedMilliseconds}ms): {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private (string version, JsonArray assets, JsonObject asset, string assetName, string platform, string arch)
        SelectBestAsset(JsonObject release)
    {
        Logger.MethodEntry();
        
        var tag = GetNodeString(release["tag_name"]);
        Logger.Debug($"Release tag: {tag}");
        
        var m = Regex.Match(tag, "v(\\d+\\.\\d+\\.\\d+)-\\d+");
        string version = m.Success ? m.Groups[1].Value : tag.TrimStart('v');
        Logger.Debug($"解析版本: {version}");
        
        var assets = release["assets"] as JsonArray ?? new JsonArray();
        Logger.Debug($"可用资产数量: {assets.Count}");

        string arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X86 => "386",
            Architecture.Arm => "arm",
            Architecture.Arm64 => "arm64",
            _ => "amd64"
        };
        Logger.Debug($"OS 架构: {RuntimeInformation.OSArchitecture} -> {arch}");

        string platform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" :
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" :
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "darwin" : "linux";
        Logger.Debug($"平台: {platform}");

        string basePattern = $"frp_LoCyanFrp-{version}_{platform}_{arch}";
        Logger.Debug($"匹配模式: {basePattern}");
        
        var assetObjects = assets.OfType<JsonObject>().ToList();
        var candidates = assetObjects
            .Where(a => GetAssetName(a).StartsWith(basePattern, StringComparison.OrdinalIgnoreCase))
            .ToList();
        Logger.Debug($"精确匹配候选: {candidates.Count}");

        if (candidates.Count == 0)
        {
            Logger.Debug($"尝试模糊匹配: {platform}_{arch}");
            candidates = assetObjects.Where(a => GetAssetName(a).Contains($"{platform}_{arch}")).ToList();
            Logger.Debug($"模糊匹配候选: {candidates.Count}");
        }

        if (candidates.Count == 0)
        {
            Logger.Warning("未找到匹配的资产，使用第一个可用资产");
            var any = assetObjects.FirstOrDefault() ?? throw new Exception("未找到可用资产");
            Logger.MethodExit($"{GetAssetName(any)}");
            return (version, assets, any, GetAssetName(any), platform, arch);
        }

        JsonObject? pick = null;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Logger.Debug("优先选择 .zip 格式 (Windows)");
            pick = candidates.FirstOrDefault(a => GetAssetName(a).EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            Logger.Debug("优先选择 .tar.gz 格式 (Unix)");
            pick = candidates.FirstOrDefault(a => GetAssetName(a).EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
                   ?? candidates.FirstOrDefault(a => GetAssetName(a).EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        }

        pick ??= candidates.First();
        Logger.Info($"选中资产: {GetAssetName(pick)}");
        Logger.MethodExit($"{GetAssetName(pick)}");
        return (version, assets, pick, GetAssetName(pick), platform, arch);
    }

    private async Task DownloadFileAsync(string url, string destPath, CancellationToken token)
    {
        Logger.MethodEntry($"url={url}, destPath={destPath}");
        var sw = Stopwatch.StartNew();
        
        Logger.HttpRequest("GET", url);
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        Logger.HttpResponse("GET", url, (int)response.StatusCode);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        Logger.Debug($"文件大小: {(totalBytes > 0 ? FormatBytes(totalBytes) : "未知")}");
        
        var receivedBytes = 0L;
        var lastProgressUpdate = DateTime.UtcNow;

        Logger.FileOperation("创建", destPath);
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

        sw.Stop();
        var speedMbps = totalBytes > 0 ? (totalBytes / 1024.0 / 1024.0) / (sw.ElapsedMilliseconds / 1000.0) : 0;
        Logger.Info($"下载完成: {FormatBytes(receivedBytes)}, 耗时: {sw.ElapsedMilliseconds}ms, 平均速度: {speedMbps:F2} MB/s");
        Console.WriteLine();
        Console.WriteLine($"[下载] 下载完成: {FormatBytes(receivedBytes)}");
        Logger.MethodExit();
    }

    private async Task VerifyChecksumAsync(string filePath, JsonArray assets, string assetName, 
        CancellationToken token, string releaseName)
    {
        Logger.MethodEntry($"file={Path.GetFileName(filePath)}");
        
        string assetPrefix = assetName.Split('.')[0];
        Logger.Debug($"校验文件前缀: {assetPrefix}");
        
        var assetObjects = assets.OfType<JsonObject>().ToList();

        JsonObject? checksumAsset = assetObjects.FirstOrDefault(a =>
                GetAssetName(a).EndsWith(".sha256", StringComparison.OrdinalIgnoreCase) &&
                GetAssetName(a).Contains(assetPrefix))
            ?? assetObjects.FirstOrDefault(a =>
                GetAssetName(a).EndsWith(".md5", StringComparison.OrdinalIgnoreCase) &&
                GetAssetName(a).Contains(assetPrefix));

        if (checksumAsset == null)
        {
            Logger.Debug("未找到校验文件，跳过校验");
            Console.WriteLine("[校验] 未提供校验文件，跳过校验");
            Logger.MethodExit("跳过");
            return;
        }
        
        var checksumFileName = GetAssetName(checksumAsset);
        Logger.Debug($"找到校验文件: {checksumFileName}");

        string checksumUrl = GetNodeString(checksumAsset["browser_download_url"]);
        if (string.IsNullOrWhiteSpace(checksumUrl))
        {
            Logger.Warning("校验文件链接缺失");
            Console.WriteLine("[校验] 校验文件链接缺失，跳过校验");
            Logger.MethodExit("跳过");
            return;
        }

        if (CliConfigManager.Config.UsingDownloadMirror)
        {
            checksumUrl = "https://mirrors.locyan.cn/github-release/LoCyan-Team/LoCyanFrpPureApp/" +
                          Uri.EscapeDataString(releaseName) + "/" + Uri.EscapeDataString(checksumFileName);
            Logger.Debug($"使用镜像校验地址: {checksumUrl}");
        }

        Logger.HttpRequest("GET", checksumUrl);
        var checksumContent = await _http.GetStringAsync(checksumUrl, token);
        Logger.Debug($"校验文件内容长度: {checksumContent.Length} 字节");
        
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
            Logger.Warning("校验文件中未找到有效哈希");
            Console.WriteLine("[校验] 校验文件无有效哈希，跳过校验");
            Logger.MethodExit("跳过");
            return;
        }
        
        Logger.Debug($"预期哈希 ({(sha256 ? "SHA256" : "MD5")}): {expectedHash}");

        Logger.Debug("计算文件哈希...");
        string actualHash;
        await using (var fs = File.OpenRead(filePath))
        {
            actualHash = sha256 
                ? Convert.ToHexString(await SHA256.HashDataAsync(fs, token)).ToLowerInvariant()
                : Convert.ToHexString(await MD5.HashDataAsync(fs, token)).ToLowerInvariant();
        }
        Logger.Debug($"实际哈希: {actualHash}");

        if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
        {
            Logger.Error($"哈希不匹配! 预期: {expectedHash}, 实际: {actualHash}");
            throw new Exception("文件哈希不匹配");
        }

        Logger.Info("文件校验通过");
        Console.WriteLine("[校验] 校验通过");
        Logger.MethodExit("通过");
    }

    private async Task<string> ExtractAsync(string archivePath, string workDir)
    {
        Logger.MethodEntry($"archive={Path.GetFileName(archivePath)}");
        
        string extractDir = Path.Combine(workDir, "extract");
        Logger.Debug($"解压目录: {extractDir}");
        
        if (Directory.Exists(extractDir))
        {
            Logger.Debug("删除已存在的解压目录");
            Directory.Delete(extractDir, true);
        }
        Directory.CreateDirectory(extractDir);

        if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            Logger.Debug("解压 ZIP 文件");
            ZipFile.ExtractToDirectory(archivePath, extractDir);
        }
        else if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            Logger.Debug("解压 TAR.GZ 文件");
            await ExtractTarGzAsync(archivePath, extractDir);
        }
        else
        {
            Logger.Error($"不支持的压缩格式: {Path.GetExtension(archivePath)}");
            throw new Exception("不支持的压缩格式");
        }

        string exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "frpc.exe" : "frpc";
        Logger.Debug($"查找可执行文件: {exeName}");
        
        var frpcPath = Directory.GetFiles(extractDir, exeName, SearchOption.AllDirectories).FirstOrDefault();
        if (frpcPath == null)
        {
            Logger.Error($"未找到 {exeName}");
            throw new Exception("未找到 frpc 可执行文件");
        }
        Logger.Debug($"找到 frpc: {frpcPath}");

        string finalPath = Path.Combine(workDir, exeName);
        Logger.FileOperation("复制", $"{frpcPath} -> {finalPath}");
        File.Copy(frpcPath, finalPath, true);

        // 设置执行权限
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                Logger.Debug("设置执行权限: chmod +x");
                var psi = new ProcessStartInfo
                {
                    FileName = "/bin/chmod",
                    Arguments = $"+x \"{finalPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Logger.ProcessStart("/bin/chmod", $"+x \"{finalPath}\"");
                using var process = Process.Start(psi);
                process?.WaitForExit(3000);
                if (process != null)
                {
                    Logger.ProcessExit(process.Id, process.ExitCode);
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"设置执行权限失败: {ex.Message}");
            }
        }

        // 清理
        try
        {
            Logger.Debug("清理临时文件");
            Logger.FileOperation("删除", archivePath);
            File.Delete(archivePath);
            Logger.FileOperation("删除目录", extractDir);
            Directory.Delete(extractDir, true);
        }
        catch (Exception ex)
        {
            Logger.Warning($"清理临时文件失败: {ex.Message}");
        }

        Logger.Info($"frpc 最终路径: {finalPath}");
        Logger.MethodExit(finalPath);
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
