using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Kairo.Utils.Configuration;

namespace Kairo.Utils
{
    internal sealed class FrpcUpdateResult
    {
        public bool Skipped { get; init; }
        public bool UpdateAvailable { get; init; }
        public string? LocalVersion { get; init; }
        public string? RemoteVersion { get; init; }
        public string? Message { get; init; }
    }

    internal static class FrpcUpdateChecker
    {
        private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
        private const string ApiMirror = "https://hub.locyan.cloud/repos/LoCyan-Team/LocyanFrpPureApp/releases/latest";
        private const string ApiOrigin = "https://api.github.com/repos/LoCyan-Team/LocyanFrpPureApp/releases/latest";

        public static bool ShouldCheckNow()
        {
            long last = Global.Config.FrpcLastUpdateCheckUtc;
            if (last <= 0) return true;
            try
            {
                var lastTime = DateTimeOffset.FromUnixTimeSeconds(last);
                return DateTimeOffset.UtcNow - lastTime >= CheckInterval;
            }
            catch
            {
                return true;
            }
        }

        public static bool IsManagedFrpcPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            try
            {
                var full = Path.GetFullPath(path);
                var workDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Kairo", "frpc");
                workDir = Path.GetFullPath(workDir);
                return full.StartsWith(workDir + Path.DirectorySeparatorChar, StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        public static async Task<FrpcUpdateResult> CheckAsync(CancellationToken ct = default)
        {
            if (!ShouldCheckNow())
                return new FrpcUpdateResult { Skipped = true, Message = "recently checked" };

            string frpcPath = Global.Config.FrpcPath;
            if (string.IsNullOrWhiteSpace(frpcPath) || !File.Exists(frpcPath))
                return new FrpcUpdateResult { Skipped = true, Message = "frpc not found" };

            UpdateLastCheckUtc();

            string? localVersion = await TryGetLocalVersionAsync(frpcPath, ct);
            if (string.IsNullOrWhiteSpace(localVersion))
                return new FrpcUpdateResult { Skipped = true, Message = "local version unknown" };

            UpdateLocalVersion(localVersion);

            JsonObject? release = await TryFetchReleaseAsync(ct);
            if (release == null)
            {
                return new FrpcUpdateResult
                {
                    Skipped = true,
                    Message = "release unavailable",
                    LocalVersion = localVersion
                };
            }

            string remoteVersion = ExtractReleaseVersion(release);
            if (string.IsNullOrWhiteSpace(remoteVersion))
            {
                return new FrpcUpdateResult
                {
                    Skipped = true,
                    Message = "remote version unknown",
                    LocalVersion = localVersion
                };
            }

            bool updateAvailable = !string.Equals(localVersion, remoteVersion, StringComparison.OrdinalIgnoreCase);
            return new FrpcUpdateResult
            {
                UpdateAvailable = updateAvailable,
                LocalVersion = localVersion,
                RemoteVersion = remoteVersion
            };
        }

        private static async Task<JsonObject?> TryFetchReleaseAsync(CancellationToken ct)
        {
            using var api = new ApiClient();
            return await TryFetchAsync(api, ApiMirror, ct) ?? await TryFetchAsync(api, ApiOrigin, ct);
        }

        private static async Task<JsonObject?> TryFetchAsync(ApiClient api, string url, CancellationToken ct)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(8));
                using var resp = await api.GetWithoutAuthAsync(url, cts.Token);
                var text = await resp.Content.ReadAsStringAsync(cts.Token);
                return JsonNode.Parse(text) as JsonObject;
            }
            catch
            {
                return null;
            }
        }

        private static string ExtractReleaseVersion(JsonObject release)
        {
            var tag = GetNodeString(release["tag_name"]);
            if (string.IsNullOrWhiteSpace(tag)) return string.Empty;

            var match = Regex.Match(tag, "v?(\\d+\\.\\d+\\.\\d+[a-zA-Z0-9]*)");
            if (match.Success) return match.Groups[1].Value;
            return tag.TrimStart('v');
        }

        private static async Task<string?> TryGetLocalVersionAsync(string frpcPath, CancellationToken ct)
        {
            try
            {
                if (ct.IsCancellationRequested) return null;
                var psi = new ProcessStartInfo
                {
                    FileName = frpcPath,
                    Arguments = "-v",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) return null;

                var outputTask = proc.StandardOutput.ReadToEndAsync();
                var errorTask = proc.StandardError.ReadToEndAsync();

                if (!proc.WaitForExit(2000))
                {
                    try { proc.Kill(true); } catch { }
                    return null;
                }

                var output = (await outputTask) + "\n" + (await errorTask);
                return ParseVersionFromText(output);
            }
            catch
            {
                return null;
            }
        }

        private static string? ParseVersionFromText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var match = Regex.Match(text, "(\\d+\\.\\d+\\.\\d+[a-zA-Z0-9]*)");
            return match.Success ? match.Groups[1].Value : null;
        }

        private static void UpdateLastCheckUtc()
        {
            ConfigManager.TryUpdate(cfg =>
            {
                cfg.FrpcLastUpdateCheckUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                return true;
            }, save: true, debounce: false);
        }

        private static void UpdateLocalVersion(string localVersion)
        {
            ConfigManager.TryUpdate(cfg =>
            {
                if (string.Equals(cfg.FrpcVersion, localVersion, StringComparison.OrdinalIgnoreCase))
                    return false;
                cfg.FrpcVersion = localVersion;
                return true;
            }, save: true, debounce: true);
        }

        private static string GetNodeString(JsonNode? node)
        {
            if (node is JsonValue value && value.TryGetValue<string>(out var str))
                return str;
            return node?.ToString() ?? string.Empty;
        }
    }
}
