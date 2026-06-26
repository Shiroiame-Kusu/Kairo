using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Kairo.Core.Models;
using Kairo.Core.Providers;
using Kairo.Cli.Configuration;
using Kairo.Cli.Services;
using Kairo.Cli.Utils;

namespace Kairo.Cli;

internal sealed class CliOAuthFlow
{
    private readonly Func<IFrpProvider> _providerFactory;
    private string _pkceCodeVerifier = string.Empty;

    public CliOAuthFlow(Func<IFrpProvider> providerFactory)
    {
        _providerFactory = providerFactory;
    }

    public async Task<bool> InteractiveLoginAsync(ApiClient apiClient)
    {
        Logger.MethodEntry();
        var provider = _providerFactory();
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("                     欢迎使用 Kairo CLI");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();
        Console.WriteLine("检测到您尚未登录，需要先完成 OAuth 授权。");
        Console.WriteLine();

        if (!provider.SupportsOAuthLogin)
        {
            Console.WriteLine($"[错误] {provider.DisplayName} 未公开 OAuth 登录接口");
            Logger.MethodExit("false (provider unsupported)");
            return false;
        }

        var codeChallenge = string.Empty;
        if (provider.Type == FrpProviderType.Lolia)
        {
            _pkceCodeVerifier = CreatePkceCodeVerifier();
            codeChallenge = CreatePkceCodeChallenge(_pkceCodeVerifier);
        }

        var oauthUrl = BuildOAuthUrl(provider, codeChallenge);
        Logger.Debug($"OAuth URL: {oauthUrl}");
        Console.WriteLine("请在浏览器中打开以下链接进行授权:");
        Console.WriteLine();
        Console.WriteLine($"  {oauthUrl}");
        Console.WriteLine();

        Console.Write("是否自动打开浏览器? [Y/n]: ");
        var autoOpen = Console.ReadLine()?.Trim().ToLowerInvariant();
        if (autoOpen != "n" && autoOpen != "no")
            TryOpenBrowser(oauthUrl);

        Console.WriteLine();
        Console.WriteLine("授权完成后，请将获取到的授权码粘贴到下方:");
        Console.Write("授权码: ");
        var code = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            Console.WriteLine("[错误] 授权码不能为空");
            Logger.MethodExit("false (授权码为空)");
            return false;
        }

        var result = await PerformLoginWithCodeAsync(apiClient, code, _pkceCodeVerifier);
        Logger.MethodExit(result.ToString());
        return result;
    }

    public async Task<bool> PerformLoginWithCodeAsync(ApiClient apiClient, string code, string codeVerifier = "")
    {
        Logger.MethodEntry($"code长度={code.Length}");
        Console.WriteLine("[信息] 正在使用 OAuth Code 获取令牌...");
        var loginResult = await apiClient.ExchangeCodeForRefreshTokenAsync(code, codeVerifier);
        if (!loginResult.Success)
        {
            Console.WriteLine($"[错误] 登录失败: {loginResult.Message}");
            Console.WriteLine();
            Console.WriteLine("[提示] 请重新获取授权码:");
            ShowOAuthUrl();
            Logger.MethodExit("false");
            return false;
        }

        Console.WriteLine($"[成功] 登录成功! 用户: {loginResult.Username}");
        Console.WriteLine("[信息] 凭据已保存到配置文件");
        Console.WriteLine();
        Logger.MethodExit("true");
        return true;
    }

    public async Task<bool> PerformLoginWithRefreshTokenAsync(ApiClient apiClient, string refreshToken)
    {
        Logger.MethodEntry($"refreshToken长度={refreshToken.Length}");
        Console.WriteLine("[信息] 正在使用 Refresh Token 进行登录...");
        var loginResult = await apiClient.LoginWithRefreshTokenAsync(refreshToken);
        if (!loginResult.Success)
        {
            Console.WriteLine($"[错误] 登录失败: {loginResult.Message}");
            Console.WriteLine();
            Console.WriteLine("[提示] Refresh Token 可能已过期，请重新获取授权码:");
            ShowOAuthUrl();
            Logger.MethodExit("false");
            return false;
        }

        Console.WriteLine($"[成功] 登录成功! 用户: {loginResult.Username}");
        Console.WriteLine("[信息] 凭据已保存到配置文件");
        Console.WriteLine();
        Logger.MethodExit("true");
        return true;
    }

    public void ShowOAuthUrl()
    {
        var provider = _providerFactory();
        if (!provider.SupportsOAuthLogin)
        {
            Console.WriteLine($"[错误] {provider.DisplayName} 未公开 OAuth 登录接口");
            return;
        }
        if (provider.Type == FrpProviderType.Lolia)
        {
            Console.WriteLine("[错误] LoliaFRP OAuth 使用 PKCE，请使用交互式登录生成配套 code_verifier");
            return;
        }

        var oauthUrl = BuildOAuthUrl(provider, string.Empty);
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("请在浏览器中打开以下 URL 进行授权:");
        Console.WriteLine();
        Console.WriteLine(oauthUrl);
        Console.WriteLine();
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();
        Console.WriteLine("授权完成后，您将获得一个授权码 (Code)。");
        Console.WriteLine("请复制该授权码，然后使用以下命令完成登录:");
        Console.WriteLine();
        Console.WriteLine("  kairo-cli --code <your_code>");
    }

    private static string BuildOAuthUrl(IFrpProvider provider, string codeChallenge) => provider.BuildOAuthUrl(new OAuthRequest
    {
        Scopes = provider.Type == FrpProviderType.Lolia ? "all node:read" : "User,Node,Tunnel,Sign",
        RedirectUri = provider.Type == FrpProviderType.Lolia ? BuildLoopbackCallbackUri() : string.Empty,
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
                using (Process.Start(new ProcessStartInfo("cmd", $"/c start {url.Replace("&", "^&")}") { UseShellExecute = false })) { }

            Console.WriteLine("[信息] 已尝试打开浏览器");
        }
        catch
        {
            Console.WriteLine("[提示] 无法自动打开浏览器，请手动复制链接");
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
