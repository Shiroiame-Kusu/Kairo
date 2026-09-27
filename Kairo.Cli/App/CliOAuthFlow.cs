using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Kairo.Core;
using Kairo.Core.Localization;
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
        ConsoleUi.Section(L.T("cli.oauth.signInTitle", provider.DisplayName));

        if (!provider.SupportsOAuthLogin)
        {
            ConsoleUi.Error(L.T("cli.oauth.unsupported", provider.DisplayName));
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

        Console.WriteLine(L.T("cli.oauth.step1"));
        ConsoleUi.WriteLine("   " + oauthUrl, ConsoleColor.Cyan);
        Console.WriteLine();

        if (EnvironmentDetector.IsLinuxHeadless())
            ConsoleUi.Dim(L.T("cli.oauth.headless"));
        else if (ConsoleUi.Confirm(L.T("cli.oauth.askOpenBrowser")))
            TryOpenBrowser(oauthUrl);

        Console.WriteLine();
        string? input;
        OAuthCallbackResult? callback = null;
        if (listener != null)
        {
            Console.WriteLine(L.T("cli.oauth.step2Loopback"));
            ConsoleUi.Dim(L.T("cli.oauth.pasteHint"));
            using var cts = new CancellationTokenSource();
            var callbackTask = listener.WaitForCallbackAsync(cts.Token);
            input = await ConsoleUi.ReadLineUntilAsync(L.T("cli.oauth.codeOrUrl"), callbackTask);
            if (input == null)
                callback = await callbackTask;
            cts.Cancel();
        }
        else
        {
            if (usesLoopback)
                ConsoleUi.Warn(L.T("cli.oauth.loopbackFailed", CliConfigManager.Config.OAuthPort));
            Console.WriteLine(L.T("cli.oauth.step2Code"));
            input = ConsoleUi.Prompt(L.T("cli.oauth.code"));
        }

        var (code, refreshToken, error) = callback != null
            ? (callback.Code, callback.RefreshToken, callback.Error)
            : ParseAuthorizationInput(input);

        if (callback != null && string.IsNullOrEmpty(error))
            ConsoleUi.Success(L.T("cli.oauth.callbackReceived"));

        if (!string.IsNullOrEmpty(error))
        {
            ConsoleUi.Error(L.T("cli.oauth.denied", error));
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
            ConsoleUi.Error(L.T("cli.oauth.emptyCode"));
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

        ConsoleUi.Info(L.T("cli.oauth.verifying"));
        var loginResult = await apiClient.ExchangeCodeForRefreshTokenAsync(parsedCode, codeVerifier, redirectUri);
        if (!loginResult.Success)
        {
            ConsoleUi.Error(L.T("cli.oauth.failed", loginResult.Message));
            ConsoleUi.Hint(loginResult.IsNetworkError
                ? L.T("cli.oauth.networkRetryLogin", apiClient.Provider.DisplayName)
                : L.T("cli.oauth.codeExpired"));
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
        ConsoleUi.Info(L.T("cli.oauth.usingRefreshToken"));
        var loginResult = await apiClient.LoginWithRefreshTokenAsync(refreshToken);
        if (!loginResult.Success)
        {
            ConsoleUi.Error(L.T("cli.oauth.failed", loginResult.Message));
            ConsoleUi.Hint(loginResult.IsNetworkError
                ? L.T("cli.oauth.networkRetry", apiClient.Provider.DisplayName)
                : L.T("cli.oauth.refreshExpired"));
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
            ConsoleUi.Error(L.T("cli.oauth.unsupported", provider.DisplayName));
            return;
        }
        if (provider.Type == FrpProviderType.Lolia)
        {
            ConsoleUi.Warn(L.T("cli.oauth.pkceInteractive", provider.DisplayName));
            ConsoleUi.Command("kairo-cli login", L.T("cli.oauth.followPrompts"));
            return;
        }

        var oauthUrl = BuildOAuthUrl(provider, string.Empty, string.Empty);
        ConsoleUi.Section(L.T("cli.oauth.authorizeTitle", provider.DisplayName));
        Console.WriteLine(L.T("cli.oauth.openLink"));
        ConsoleUi.WriteLine("  " + oauthUrl, ConsoleColor.Cyan);
        Console.WriteLine();
        Console.WriteLine(L.T("cli.oauth.thenRun"));
        ConsoleUi.Command(L.T("cli.oauth.codeCommand"));
    }

    private static void ReportLoginSuccess(IFrpProvider provider, string? username)
    {
        ConsoleUi.Success(L.T("cli.oauth.signedIn", provider.DisplayName, username));
        ConsoleUi.Dim(L.T("cli.oauth.savedTo", Kairo.Core.Configuration.ConfigHelper.GetSettingsFilePath()));
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

            ConsoleUi.Dim(L.T("cli.oauth.browserOpened"));
        }
        catch (Exception ex)
        {
            Logger.Debug($"打开浏览器失败: {ex.Message}");
            ConsoleUi.Hint(L.T("cli.oauth.browserFailed"));
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
