using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Updater
{
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            Console.WriteLine("Kairo Updater starting...");
            int pid = args.Length > 0 && int.TryParse(args[0], out var p) ? p : -1;
            string owner = args.Length > 1 ? args[1] : "Shiroiame-Kusu";
            string repo = args.Length > 2 ? args[2] : "Kairo";
            string branch = args.Length > 3 ? args[3] : string.Empty; // Stable/Beta/Alpha
            var installDir = AppContext.BaseDirectory; // expected to be alongside Kairo binaries

            if (pid > 0)
            {
                try
                {
                    Console.WriteLine($"Waiting for process {pid} to exit...");
                    var proc = Process.GetProcessById(pid);
                    proc.WaitForExit(60_000); // wait up to 60s
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warn: cannot wait for pid {pid}: {ex.Message}");
                }
            }

            try
            {
                var assetUrl = await GetLatestAssetUrl(owner, repo, branch);
                if (string.IsNullOrEmpty(assetUrl))
                {
                    Console.WriteLine("No suitable asset found in releases.");
                    return 2;
                }
                Console.WriteLine($"Downloading: {assetUrl}");
                var tmpPath = Path.Combine(installDir, "update.tmp");
                if (File.Exists(tmpPath)) TryDelete(tmpPath);
                await DownloadFileAsync(assetUrl, tmpPath);

                if (assetUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("Extracting ZIP...");
                    var extractDir = Path.Combine(installDir, "update_extract");
                    if (Directory.Exists(extractDir)) TryDeleteDir(extractDir);
                    ZipFile.ExtractToDirectory(tmpPath, extractDir, overwriteFiles: true);
                    // If there's a single top-level directory, use it as root
                    var topEntries = Directory.GetFileSystemEntries(extractDir);
                    string sourceRoot = extractDir;
                    if (topEntries.Length == 1 && Directory.Exists(topEntries[0]))
                    {
                        sourceRoot = topEntries[0];
                    }
                    Console.WriteLine("Copying files...");
                    CopyAll(new DirectoryInfo(sourceRoot), new DirectoryInfo(installDir));
                    TryDelete(tmpPath);
                    TryDeleteDir(extractDir);
                }
                else
                {
                    // Fallback: if it looks like a single binary/package, try to replace Kairo.* accordingly
                    Console.WriteLine("Applying single-file update...");
                    var fileName = Path.GetFileName(assetUrl);
                    var dest = Path.Combine(installDir, fileName);
                    TryDelete(dest);
                    File.Move(tmpPath, dest);
                }
                Console.WriteLine("Update applied.");

                // Try to restart Kairo
                TryRestartKairo(installDir);
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Update failed: {ex}");
                return 1;
            }
        }

        private static async Task<string?> GetLatestAssetUrl(string owner, string repo, string branch)
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Kairo-Updater/1.0");
            // list releases to select by branch
            var url = $"https://api.github.com/repos/{owner}/{repo}/releases";
            var resp = await http.GetAsync(url);
            resp.EnsureSuccessStatusCode();
            using var stream = await resp.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            string? desired = NormalizeBranch(branch);
            JsonElement? chosen = null;
            foreach (var release in doc.RootElement.EnumerateArray())
            {
                var tag = release.GetProperty("tag_name").GetString() ?? string.Empty;
                if (IsBranchMatch(tag, desired)) { chosen = release; break; }
            }
            if (chosen == null)
            {
                // Fallback to latest
                chosen = doc.RootElement.GetArrayLength() > 0 ? doc.RootElement[0] : (JsonElement?)null;
                if (chosen == null) return null;
            }
            if (!chosen.Value.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                return null;
            string osHint = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "mac" : "linux";
            string? best = null;
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? string.Empty;
                var download = asset.GetProperty("browser_download_url").GetString();
                if (string.IsNullOrEmpty(download)) continue;
                var n = name.ToLowerInvariant();
                if (n.EndsWith(".zip") && n.Contains(osHint))
                {
                    best = download; break;
                }
                if (best == null && n.EndsWith(".zip")) best = download;
            }
            if (best == null)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var download = asset.GetProperty("browser_download_url").GetString();
                    if (!string.IsNullOrEmpty(download)) return download;
                }
            }
            return best;
        }

        private static string? NormalizeBranch(string? b)
        {
            if (string.IsNullOrWhiteSpace(b)) return null;
            b = b.Trim().ToLowerInvariant();
            return b switch { "alpha" => "alpha", "beta" => "beta", "rc" => "releasecandidate", "releasecandidate" => "releasecandidate", "release" => "release", _ => null };
        }

        private static bool IsBranchMatch(string tag, string? desired)
        {
            // Tags like v3.1.0-beta.1, v3.1.0-alpha.2, v3.1.0-rc.3, v3.1.0-release.1
            var lower = tag.ToLowerInvariant();
            if (desired == null) return true;
            return desired switch
            {
                "alpha" => lower.Contains("-alpha."),
                "beta" => lower.Contains("-beta."),
                "releasecandidate" => lower.Contains("-rc."),
                "release" => lower.Contains("-release."),
                _ => true
            };
        }

        private static async Task DownloadFileAsync(string url, string dest)
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Kairo-Updater/1.0");
            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();
            await using var fs = File.Create(dest);
            await using var stream = await resp.Content.ReadAsStreamAsync();
            await stream.CopyToAsync(fs);
        }

        private static void CopyAll(DirectoryInfo source, DirectoryInfo target)
        {
            foreach (var dir in source.GetDirectories())
            {
                var targetSub = target.CreateSubdirectory(dir.Name);
                CopyAll(dir, targetSub);
            }
            foreach (var file in source.GetFiles())
            {
                // avoid overwriting the running updater itself
                if (file.Name.StartsWith("Updater", StringComparison.OrdinalIgnoreCase))
                    continue;
                var dest = Path.Combine(target.FullName, file.Name);
                TryDelete(dest);
                file.CopyTo(dest, overwrite: true);
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private static void TryDeleteDir(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
        }

        private static void TryRestartKairo(string installDir)
        {
            try
            {
                string exe = Path.Combine(installDir, OperatingSystem.IsWindows() ? "Kairo.exe" : "Kairo");
                string dll = Path.Combine(installDir, "Kairo.dll");
                ProcessStartInfo? psi = null;
                if (File.Exists(exe))
                {
                    psi = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = installDir };
                }
                else if (File.Exists(dll))
                {
                    psi = new ProcessStartInfo("dotnet")
                    {
                        UseShellExecute = false,
                        WorkingDirectory = installDir,
                        ArgumentList = { dll }
                    };
                }
                if (psi != null)
                {
                    Console.WriteLine("Restarting Kairo...");
                    Process.Start(psi);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warn: failed to restart Kairo: {ex.Message}");
            }
        }
    }
}
