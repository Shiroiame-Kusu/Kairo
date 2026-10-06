using Kairo.Core;
using Kairo.Core.Localization;
using Kairo.Core.Providers;
using Kairo.Cli.Utils;

namespace Kairo.Cli;

internal static class CliHelpWriter
{
    private static string ProviderIds => string.Join(" / ", FrpProviderRegistry.All.Select(p => p.Id));

    public static void ShowBanner()
    {
        Console.WriteLine();
        ConsoleUi.Write("  Kairo CLI ", ConsoleColor.Cyan);
        ConsoleUi.Write($"{AppConstants.Version} \"{AppConstants.VersionName}\"", ConsoleColor.White);
        ConsoleUi.WriteLine($"  {AppConstants.Branch.ToDisplayName()} {AppConstants.Revision}", ConsoleColor.DarkGray);
    }

    public static void ShowHelp()
    {
        Section(L.T("cli.help.usage"));
        Console.WriteLine("  " + L.T("cli.help.usageLine"));

        Section(L.T("cli.help.commands"));
        Item(L.T("cli.help.cmd.none"), L.T("cli.help.cmd.noneDescription"));
        Item("start [ID...]", L.T("cli.help.cmd.start"));
        Item("list, ls", L.T("cli.help.cmd.list"));
        Item("login", L.T("cli.help.cmd.login"));
        Item("logout", L.T("cli.help.cmd.logout"));
        Item("status, whoami", L.T("cli.help.cmd.status"));
        Item(L.T("cli.help.cmd.providerSyntax"), L.T("cli.help.cmd.provider", ProviderIds));
        Item("help, version", L.T("cli.help.cmd.help"));

        Section(L.T("cli.help.options"));
        Item(L.T("cli.help.opt.providerSyntax"), L.T("cli.help.opt.provider"));
        Item("-p, --proxy <ID,...>", L.T("cli.help.opt.proxy"));
        Item("-l, --list", L.T("cli.help.opt.list"));
        Item("--oauth", L.T("cli.help.opt.oauth"));
        Item(L.T("cli.help.opt.codeSyntax"), L.T("cli.help.opt.code"));
        Item(L.T("cli.help.opt.refreshTokenSyntax"), L.T("cli.help.opt.refreshToken"));
        Item(L.T("cli.help.opt.frpTokenSyntax"), L.T("cli.help.opt.frpToken"));
        Item(L.T("cli.help.opt.frpcPathSyntax"), L.T("cli.help.opt.frpcPath"));
        Item("--github, --no-mirror", L.T("cli.help.opt.github"));
        Item("--no-interactive", L.T("cli.help.opt.noInteractive"));
        Item(L.T("cli.help.opt.langSyntax"), L.T("cli.help.opt.lang"));
        Item("--no-color", L.T("cli.help.opt.noColor"));
        Item("-d, --debug", L.T("cli.help.opt.debug"));
        Item("--log-file", L.T("cli.help.opt.logFile"));
        Item("-q, --quiet", L.T("cli.help.opt.quiet"));
        Item("-v, --version", L.T("cli.help.opt.version"));
        Item("-h, --help", L.T("cli.help.opt.help"));

        Section(L.T("cli.help.examples"));
        ConsoleUi.Command("kairo-cli", L.T("cli.help.example.firstRun"));
        ConsoleUi.Command("kairo-cli provider lolia", L.T("cli.help.example.switchLolia"));
        ConsoleUi.Command("kairo-cli --provider locyan list", L.T("cli.help.example.switchAndList"));
        ConsoleUi.Command("kairo-cli start 1,2", L.T("cli.help.example.startIds"));
        ConsoleUi.Command("kairo-cli start --no-interactive", L.T("cli.help.example.startAll"));
        ConsoleUi.Command("kairo-cli --lang en-US status", L.T("cli.help.example.lang"));

        Section(L.T("cli.help.environment"));
        Item("KAIRO_CONFIG_DIR", L.T("cli.help.env.configDir"));
        Item("KAIRO_LANG", L.T("cli.help.env.lang"));
        Item("NO_COLOR", L.T("cli.help.env.noColor"));
        Console.WriteLine();
    }

    public static void ShowVersion()
    {
        var version = AppVersion.FromComponents(AppConstants.Version, AppConstants.Branch, AppConstants.Revision);
        Console.WriteLine($"Kairo CLI {AppConstants.Version} ({AppConstants.Branch.ToDisplayName()})");
        Console.WriteLine($"Version Name: {AppConstants.VersionName}");
        Console.WriteLine($"Revision: {AppConstants.Revision}");
        Console.WriteLine($"Tag: {version.ToTagString()}");
        Console.WriteLine($"Developer: {AppConstants.Developer}");
        Console.WriteLine(AppConstants.Copyright);
    }

    private static void Section(string title)
    {
        Console.WriteLine();
        ConsoleUi.WriteLine(title + ":", ConsoleColor.Yellow);
    }

    private static void Item(string name, string description)
    {
        Console.Write("  ");
        ConsoleUi.Write(ConsoleUi.PadRight(name, 28), ConsoleColor.Green);
        Console.WriteLine(" " + description);
    }
}
