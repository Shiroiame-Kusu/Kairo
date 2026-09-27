using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Kairo.Core;
using Kairo.Core.Providers;
using Kairo.Cli.Configuration;
using Kairo.Cli.Services;
using Kairo.Cli.Utils;

namespace Kairo.Cli;

internal sealed class CliOAuthFlow
{
    private readonly Func<IFrpProvider> _providerFactory;

    public CliOAuthFlow(Func<IFrpProvider> providerFactory)
    {
        _providerFactory = providerFactory;
    }

    public async Task<bool> InteractiveLoginAsync(ApiClient apiClient)
    {
        Logger.MethodEntry();
        var provider = _providerFactory();
        ConsoleUi.Section($"登录 {provider.DisplayName}");

        if (!provider.SupportsOAuthLogin)
        {
            ConsoleUi.Error($"{provider.DisplayName} 未公开 OAuth 登录接口");
            Logger.MethodExit("false (provider unsupported)");
            return false;
        }

        // LoliaFRP 使用 PKCE + 本地回调地址；LoCyanFrp 在授权页面直接显示授权码
        var usesLoopback = provider.Type == FrpProviderType.Lolia;
        using var listener = usesLoopback ? OAuthLoopbackListener.TryStart(CliConfigManager.Config.OAuthPort) : null;
        var redirectUri = usesLoopback ? listener?.RedirectUri ?? BuildLoopbackCallbackUri() : string.Empty;
        var codeVerifier = usesLoopback ? CreatePkceCodeVerifier() : string.Empty;
        var codeChallenge = usesLoopback ? CreatePkceCodeChallenge(codeVerifier) : string.Empty;

        var oauthUrl = BuildOAuthUrl(provider, redirectUri, codeChallenge);
        Logger.Debug($"OAuth URL: {oauthUrl}");

        Console.WriteLine("1. 在浏览器中打开以下链接并完成授权:");
        ConsoleUi.WriteLine("   " + oauthUrl, ConsoleColor.Cyan);
        Console.WriteLine();

        if (EnvironmentDetector.IsLinuxHeadless())
            ConsoleUi.Dim("   未检测到图形界面，请在任意设备的浏览器中打开上面的链接");
        else if (ConsoleUi.Confirm("   是否自动打开浏览器?"))
            TryOpenBrowser(oauthUrl);

        Console.WriteLine();
        string? input;
        OAuthCallbackResult? callback = null;
        if (listener != null)
        {
            Console.WriteLine("2. 授权完成后会自动返回这里。");
            ConsoleUi.Dim("   如果浏览器不在本机，请复制浏览器地址栏中的完整地址并粘贴到下方");
            using var cts = new CancellationTokenSource();
            var callbackTask = listener.WaitForCallbackAsync(cts.Token);
            input = await ConsoleUi.ReadLineUntilAsync("授权码或回调地址", callbackTask);
            if (input == null)
                callback = await callbackTask;
            cts.Cancel();
        }
        else
        {
            if (usesLoopback)
                ConsoleUi.Warn($"无法监听本地回调端口 {CliConfigManager.Config.OAuthPort}，授权后请手动粘贴浏览器地址栏中的地址");
            Console.WriteLine("2. 授权完成后，将页面显示的授权码粘贴到下方（也可以粘贴完整的回调地址）");
            input = ConsoleUi.Prompt("授权码");
        }

        var (code, refreshToken, error) = callback != null
            ? (callback.Code, callback.RefreshToken, callback.Error)
            : ParseAuthorizationInput(input);

        if (callback != null && string.IsNullOrEmpty(error))
            ConsoleUi.Success("已收到浏览器回调");

        if (!string.IsNullOrEmpty(error))
        {
            ConsoleUi.Error($"授权失败: {error}");
            Logger.MethodExit("false (授权被拒绝)");
            return false;
        }

        bool result;
        if (!string.IsNullOrWhiteSpace(refreshToken))
            result = await PerformLoginWithRefreshTokenAsync(apiClient, refreshToken);
        else if (!string.IsNullOrWhiteSpace(code))
            result = await PerformLoginWithCodeAsync(apiClient, code, codeVerifier, redirectUri);
        else
        {
            ConsoleUi.Error("授权码不能为空");
            result = false;
        }

        Logger.MethodExit(result.ToString());
        return result;
    }

    public async Task<bool> PerformLoginWithCodeAsync(ApiClient apiClient, string code, string codeVerifier = "", string? redirectUri = null)
    {
        Logger.MethodEntry($"code长度={code.Length}");
        var (parsedCode, parsedRefreshToken, _) = ParseAuthorizationInput(code);
        if (!string.IsNullOrWhiteSpace(parsedRefreshToken))
            return await PerformLoginWithRefreshTokenAsync(apiClient, parsedRefreshToken);

        ConsoleUi.Info("正在验证授权码...");
        var loginResult = await apiClient.ExchangeCodeForRefreshTokenAsync(parsedCode, codeVerifier, redirectUri);
        if (!loginResult.Success)
        {
            ConsoleUi.Error($"登录失败: {loginResult.Message}");
            ConsoleUi.Hint(loginResult.IsNetworkError
                ? $"无法连接到 {apiClient.Provider.DisplayName}，请检查网络后运行 kairo-cli login 重试"
                : "授权码只能使用一次且会很快过期，请运行 kairo-cli login 重新授权");
            Logger.MethodExit("false");
            return false;
        }

        ReportLoginSuccess(apiClient.Provider, loginResult.Username);
        Logger.MethodExit("true");
        return true;
    }

    public async Task<bool> PerformLoginWithRefreshTokenAsync(ApiClient apiClient, string refreshToken)
    {
        Logger.MethodEntry($"refreshToken长度={refreshToken.Length}");
        ConsoleUi.Info("正在使用 Refresh Token 登录...");
        var loginResult = await apiClient.LoginWithRefreshTokenAsync(refreshToken);
        if (!loginResult.Success)
        {
            ConsoleUi.Error($"登录失败: {loginResult.Message}");
            ConsoleUi.Hint(loginResult.IsNetworkError
                ? $"无法连接到 {apiClient.Provider.DisplayName}，请检查网络后重试"
                : "Refresh Token 可能已过期，请运行 kairo-cli login 重新授权");
            Logger.MethodExit("false");
            return false;
        }

        ReportLoginSuccess(apiClient.Provider, loginResult.Username);
        Logger.MethodExit("true");
        return true;
    }

    public void ShowOAuthUrl()
    {
        var provider = _providerFactory();
        if (!provider.SupportsOAuthLogin)
        {
            ConsoleUi.Error($"{provider.DisplayName} 未公开 OAuth 登录接口");
            return;
        }
        if (provider.Type == FrpProviderType.Lolia)
        {
            ConsoleUi.Warn($"{provider.DisplayName} 的授权需要在交互模式下完成（PKCE 校验需要同一会话）");
            ConsoleUi.Command("kairo-cli login", "# 按提示完成授权");
            return;
        }

        var oauthUrl = BuildOAuthUrl(provider, string.Empty, string.Empty);
        ConsoleUi.Section($"授权 {provider.DisplayName}");
        Console.WriteLine("请在浏览器中打开以下链接进行授权:");
        ConsoleUi.WriteLine("  " + oauthUrl, ConsoleColor.Cyan);
        Console.WriteLine();
        Console.WriteLine("授权完成后页面会显示授权码 (Code)，复制后执行:");
        ConsoleUi.Command("kairo-cli --code <授权码>");
    }

    private static void ReportLoginSuccess(IFrpProvider provider, string? username)
    {
        ConsoleUi.Success($"已登录 {provider.DisplayName}，用户: {username}");
        ConsoleUi.Dim($"       凭据已保存到 {Kairo.Core.Configuration.ConfigHelper.GetSettingsFilePath()}");
    }

    /// <summary>
    /// 解析用户输入：可以是授权码本身，也可以是包含 code / refresh_token 参数的完整回调地址
    /// </summary>
    private static (string Code, string RefreshToken, string Error) ParseAuthorizationInput(string? input)
    {
        var text = input?.Trim().Trim('"', '\'') ?? string.Empty;
        if (text.Length == 0) return (string.Empty, string.Empty, string.Empty);

        var queryStart = text.IndexOf('?');
        var looksLikeUrl = text.Contains("code=", StringComparison.OrdinalIgnoreCase)
                           || text.Contains("refresh_token=", StringComparison.OrdinalIgnoreCase)
                           || text.Contains("error=", StringComparison.OrdinalIgnoreCase);
        if (!looksLikeUrl) return (text, string.Empty, string.Empty);

        var query = OAuthLoopbackListener.ParseQuery(queryStart >= 0 ? text[(queryStart + 1)..].Split('#')[0] : text);
        return (
            query.GetValueOrDefault("code", string.Empty),
            query.GetValueOrDefault("refresh_token", string.Empty),
            query.GetValueOrDefault("error_description", query.GetValueOrDefault("error", string.Empty)));
    }

    private static string BuildOAuthUrl(IFrpProvider provider, string redirectUri, string codeChallenge) => provider.BuildOAuthUrl(new OAuthRequest
    {
        Scopes = provider.Type == FrpProviderType.Lolia ? "all node:read" : "User,Node,Tunnel,Sign",
        RedirectUri = redirectUri,
        Mode = "code",
        CodeChallenge = codeChallenge,
        CodeChallengeMethod = string.IsNullOrWhiteSpace(codeChallenge) ? string.Empty : "S256"
    });

    private static void TryOpenBrowser(string url)
    {
        try
        {
            if (OperatingSystem.IsLinux())
                using (Process.Start(new ProcessStartInfo("xdg-open", url) { UseShellExecute = false })) { }
            else if (OperatingSystem.IsMacOS())
                using (Process.Start(new ProcessStartInfo("open", url) { UseShellExecute = false })) { }
            else if (OperatingSystem.IsWindows())
                using (Process.Start(new ProcessStartInfo("cmd", $"/c start {url.Replace("&", "^&")}") { UseShellExecute = false, CreateNoWindow = true })) { }

            ConsoleUi.Dim("   已尝试打开浏览器");
        }
        catch (Exception ex)
        {
            Logger.Debug($"打开浏览器失败: {ex.Message}");
            ConsoleUi.Hint("无法自动打开浏览器，请手动复制链接");
        }
    }

    private static string CreatePkceCodeVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncode(bytes);
    }

    private static string CreatePkceCodeChallenge(string codeVerifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(byte[] bytes) => Convert.ToBase64String(bytes)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    private static string BuildLoopbackCallbackUri()
    {
        var port = CliConfigManager.Config.OAuthPort > 0 ? CliConfigManager.Config.OAuthPort : 10000;
        return $"http://127.0.0.1:{port}/oauth/callback";
    }
}
