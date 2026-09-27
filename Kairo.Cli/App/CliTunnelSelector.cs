using Kairo.Core.Models;
using Kairo.Core.Providers;
using Kairo.Cli.Utils;

namespace Kairo.Cli;

internal static class CliTunnelSelector
{
    private static readonly string[] Headers = { "#", "ID", "名称", "类型", "本地地址", "访问地址", "节点" };
    private static readonly int[] MaxWidths = { 4, 8, 24, 6, 21, 36, 16 };
    private static readonly int[] MinWidths = { 1, 2, 8, 4, 9, 12, 4 };
    private const int ColumnGap = 2;

    public static void ShowTunnelList(IReadOnlyList<Tunnel> tunnels, IFrpProvider provider)
    {
        ConsoleUi.Section($"{provider.DisplayName} · 共 {tunnels.Count} 个隧道");

        var rows = tunnels.Select((t, i) => new[]
        {
            (i + 1).ToString(),
            t.Id.ToString(),
            t.ProxyName,
            string.IsNullOrWhiteSpace(t.ProxyType) ? "-" : t.ProxyType.ToUpperInvariant(),
            $"{t.LocalIp}:{t.LocalPort}",
            TunnelAddress.GetPublicAddress(t),
            string.IsNullOrWhiteSpace(t.NodeInfo?.Name) ? "-" : t.NodeInfo!.Name!
        }).ToList();

        var widths = ComputeWidths(rows);

        Console.Write("  ");
        for (var c = 0; c < Headers.Length; c++)
            ConsoleUi.Write(Cell(Headers[c], widths[c], c), ConsoleColor.DarkGray);
        Console.WriteLine();
        ConsoleUi.WriteLine("  " + new string('─', widths.Sum() + ColumnGap * (widths.Length - 1)), ConsoleColor.DarkGray);

        foreach (var row in rows)
        {
            Console.Write("  ");
            for (var c = 0; c < row.Length; c++)
            {
                var text = Cell(ConsoleUi.Truncate(row[c], widths[c]), widths[c], c);
                switch (c)
                {
                    case 0:
                        ConsoleUi.Write(text, ConsoleColor.DarkGray);
                        break;
                    case 1:
                        ConsoleUi.Write(text, ConsoleColor.Cyan);
                        break;
                    case 3:
                        ConsoleUi.Write(text, TypeColor(row[c]));
                        break;
                    default:
                        Console.Write(text);
                        break;
                }
            }
            Console.WriteLine();
        }
        Console.WriteLine();
    }

    public static List<int>? InteractiveSelectTunnels(IReadOnlyList<Tunnel> tunnels)
    {
        ConsoleUi.Dim("  输入序号或隧道 ID，多个用逗号或空格分隔，支持范围（如 1-3）");
        ConsoleUi.Dim("  直接回车或输入 all 启动全部，输入 q 退出");

        while (true)
        {
            var input = ConsoleUi.Prompt("选择要启动的隧道", "all")?.ToLowerInvariant();
            if (input == null || input is "q" or "quit" or "exit")
            {
                ConsoleUi.Info("已取消");
                return null;
            }

            if (input is "all" or "a" or "*")
            {
                ConsoleUi.Info($"已选择全部 {tunnels.Count} 个隧道");
                return tunnels.Select(t => t.Id).ToList();
            }

            var selectedIds = new List<int>();
            var invalid = new List<string>();
            foreach (var part in input.Split(new[] { ',', ' ', '，' }, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (var number in ExpandRange(part, tunnels.Count, invalid))
                {
                    var id = ResolveTunnelId(tunnels, number);
                    if (id == null) invalid.Add(number.ToString());
                    else if (!selectedIds.Contains(id.Value)) selectedIds.Add(id.Value);
                }
            }

            if (invalid.Count > 0)
                ConsoleUi.Warn($"未找到: {string.Join(", ", invalid)}");

            if (selectedIds.Count > 0)
            {
                var names = selectedIds.Select(id => tunnels.First(t => t.Id == id).ProxyName);
                ConsoleUi.Info($"已选择 {selectedIds.Count} 个隧道: {string.Join(", ", names)}");
                return selectedIds;
            }

            ConsoleUi.Warn("没有选中任何隧道，请重新输入");
        }
    }

    private static IEnumerable<int> ExpandRange(string part, int count, List<string> invalid)
    {
        var dash = part.IndexOf('-');
        if (dash > 0 && int.TryParse(part[..dash], out var from) && int.TryParse(part[(dash + 1)..], out var to))
        {
            if (from > to) (from, to) = (to, from);
            if (from < 1 || to > count)
            {
                invalid.Add(part);
                return Array.Empty<int>();
            }
            return Enumerable.Range(from, to - from + 1);
        }

        if (int.TryParse(part, out var single))
            return new[] { single };

        invalid.Add(part);
        return Array.Empty<int>();
    }

    /// <summary>
    /// 1..N 优先按序号解析，其余按隧道 ID 解析
    /// </summary>
    private static int? ResolveTunnelId(IReadOnlyList<Tunnel> tunnels, int num)
    {
        if (num >= 1 && num <= tunnels.Count)
            return tunnels[num - 1].Id;
        return tunnels.FirstOrDefault(t => t.Id == num)?.Id;
    }

    private static int[] ComputeWidths(IReadOnlyList<string[]> rows)
    {
        var widths = new int[Headers.Length];
        for (var c = 0; c < Headers.Length; c++)
        {
            var content = rows.Count == 0 ? 0 : rows.Max(r => ConsoleUi.DisplayWidth(r[c]));
            widths[c] = Math.Min(MaxWidths[c], Math.Max(ConsoleUi.DisplayWidth(Headers[c]), content));
        }

        // 终端过窄时依次收缩：访问地址 → 名称 → 节点 → 本地地址
        var available = ConsoleUi.GetWindowWidth() - 2 - ColumnGap * (widths.Length - 1);
        foreach (var column in new[] { 5, 2, 6, 4 })
        {
            var overflow = widths.Sum() - available;
            if (overflow <= 0) break;
            widths[column] = Math.Max(MinWidths[column], widths[column] - overflow);
        }
        return widths;
    }

    private static string Cell(string text, int width, int column) =>
        column == Headers.Length - 1 ? text : ConsoleUi.PadRight(text, width) + new string(' ', ColumnGap);

    private static ConsoleColor TypeColor(string type) => type.ToLowerInvariant() switch
    {
        "tcp" => ConsoleColor.Blue,
        "udp" => ConsoleColor.Magenta,
        "http" or "https" => ConsoleColor.Green,
        "xtcp" or "stcp" or "sudp" => ConsoleColor.Yellow,
        _ => ConsoleColor.Gray
    };
}
