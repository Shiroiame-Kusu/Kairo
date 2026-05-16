using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Kairo.Core.Services;
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

        public static bool ShouldCheckNow()
        {
            long last = Global.Config.FrpcLastUpdateCheckUtc;
            if (last <= 0) return true;
            try
            {
                var lastTime = DateTimeOffset.FromUnixTimeSeconds(last);
                return DateTimeOffset.UtcNow - lastTime >= CheckInterval;
            }
            catch (System.Exception ex)
            {
                AppLogger.Exception("Unhandled exception in Kairo/Utils/Update/FrpcUpdateChecker.cs:35", ex);
                return true;
            }
        }

        public static bool IsManagedFrpcPath(string? path) =>
            FrpcDownloadService.IsManagedFrpcPath(path, Global.CurrentProvider);

        public static async Task<FrpcUpdateResult> CheckAsync(CancellationToken ct = default)
        {
            if (!ShouldCheckNow())
                return new FrpcUpdateResult { Skipped = true, Message = "recently checked" };

            string frpcPath = ProviderFrpcPath.Get(Global.CurrentProvider);
            if (string.IsNullOrWhiteSpace(frpcPath) || !File.Exists(frpcPath))
                return new FrpcUpdateResult { Skipped = true, Message = "frpc not found" };

            UpdateLastCheckUtc();

            string? localVersion = await TryGetLocalVersionAsync(frpcPath, ct);
            if (string.IsNullOrWhiteSpace(localVersion))
                return new FrpcUpdateResult { Skipped = true, Message = "local version unknown" };

            UpdateLocalVersion(localVersion);

            using var http = new HttpClient();
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", $"Kairo/{Global.Version}");
            var release = await Global.CurrentProvider.GetLatestFrpcReleaseAsync(http, ct);
            if (release == null)
            {
                return new FrpcUpdateResult
                {
                    Skipped = true,
                    Message = "release unavailable",
                    LocalVersion = localVersion
                };
            }

            string remoteVersion = release.Version;
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
                    try { proc.Kill(true); }
                    catch (System.Exception ex)
                    {
                        AppLogger.Exception("Unhandled exception in Kairo/Utils/Update/FrpcUpdateChecker.cs:117", ex);
                    }
                    return null;
                }

                var output = (await outputTask) + "\n" + (await errorTask);
                return ParseVersionFromText(output);
            }
            catch (System.Exception ex)
            {
                AppLogger.Exception("Unhandled exception in Kairo/Utils/Update/FrpcUpdateChecker.cs:124", ex);
                return null;
            }
        }

        private static string? ParseVersionFromText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var match = Regex.Match(text, @"(\d+\.\d+\.\d+[a-zA-Z0-9.-]*)");
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
    }
}
