using Kairo.Core.Models;
using Kairo.Cli.Utils;

namespace Kairo.Cli;

internal static class CliTunnelSelector
{
    public static void ShowTunnelList(IReadOnlyList<Tunnel> tunnels)
    {
        Console.WriteLine();
        Console.WriteLine("可用隧道列表:");
        Console.WriteLine("─────────────────────────────────────────────────────────────────────");
        Console.WriteLine($"{"序号",-6} {"ID",-8} {"名称",-20} {"类型",-10} {"本地地址",-20} {"节点",-15}");
        Console.WriteLine("─────────────────────────────────────────────────────────────────────");

        for (var i = 0; i < tunnels.Count; i++)
        {
            var tunnel = tunnels[i];
            var localAddr = $"{tunnel.LocalIp}:{tunnel.LocalPort}";
            var nodeName = tunnel.NodeInfo?.Name ?? "未知";
            Console.WriteLine($"{i + 1,-6} {tunnel.Id,-8} {tunnel.ProxyName,-20} {tunnel.ProxyType,-10} {localAddr,-20} {nodeName,-15}");
        }

        Console.WriteLine("─────────────────────────────────────────────────────────────────────");
        Console.WriteLine();
    }

    public static List<int>? InteractiveSelectTunnels(IReadOnlyList<Tunnel> tunnels)
    {
        Console.WriteLine("请选择要启动的隧道:");
        Console.WriteLine("  - 输入序号 (例如: 1,2,3) 或隧道 ID");
        Console.WriteLine("  - 输入 'all' 或直接按回车启动全部隧道");
        Console.WriteLine("  - 输入 'q' 退出");
        Console.WriteLine();
        Console.Write("您的选择: ");

        var input = Console.ReadLine()?.Trim().ToLowerInvariant();
        if (input is "q" or "quit" or "exit")
        {
            Console.WriteLine("[信息] 已取消");
            return null;
        }

        var selectedIds = new List<int>();
        if (string.IsNullOrEmpty(input) || input is "all" or "a")
        {
            foreach (var tunnel in tunnels)
                selectedIds.Add(tunnel.Id);
            Console.WriteLine($"[信息] 已选择全部 {selectedIds.Count} 个隧道");
            return selectedIds;
        }

        foreach (var part in input.Split(',', ' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(part.Trim(), out var num)) continue;
            var id = ResolveTunnelId(tunnels, num);
            if (id.HasValue && !selectedIds.Contains(id.Value))
                selectedIds.Add(id.Value);
            else if (!id.HasValue)
            {
                Logger.Warning($"未找到隧道 ID: {num}");
                Console.WriteLine($"[警告] 未找到隧道 ID: {num}");
            }
        }

        return selectedIds;
    }

    private static int? ResolveTunnelId(IReadOnlyList<Tunnel> tunnels, int num)
    {
        if (num >= 1 && num <= tunnels.Count)
        {
            var tunnelById = tunnels.FirstOrDefault(t => t.Id == num);
            var tunnelByIndex = tunnels[num - 1];
            if (tunnelById != null && tunnelById.Id != tunnelByIndex.Id)
                return tunnelByIndex.Id;
            return tunnelById?.Id ?? tunnelByIndex.Id;
        }

        return tunnels.FirstOrDefault(t => t.Id == num)?.Id;
    }
}
