using Kairo.Core.Localization;
using Kairo.Core.Models;
using Kairo.Core.Providers;
using Kairo.Cli.Utils;

namespace Kairo.Cli;

internal static class CliTunnelSelector
{
    private static readonly string[] HeaderKeys = { "number", "id", "name", "type", "local", "address", "node" };
    private static string[] Headers => HeaderKeys.Select(key => L.T("cli.tunnels.header." + key)).ToArray();
    private static readonly int[] MaxWidths = { 4, 8, 24, 6, 21, 36, 16 };
    private static readonly int[] MinWidths = { 1, 2, 8, 4, 9, 12, 4 };
    private const int ColumnGap = 2;

    public static void ShowTunnelList(IReadOnlyList<Tunnel> tunnels, IFrpProvider provider)
    {
        ConsoleUi.Section(L.Plural("cli.tunnels.title", tunnels.Count, provider.DisplayName, tunnels.Count));

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

        var headers = Headers;
        var widths = ComputeWidths(rows, headers);

        Console.Write("  ");
        for (var c = 0; c < headers.Length; c++)
            ConsoleUi.Write(Cell(headers[c], widths[c], c), ConsoleColor.DarkGray);
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
        ConsoleUi.Dim(L.T("cli.tunnels.selectHint"));
        ConsoleUi.Dim(L.T("cli.tunnels.selectHint2"));

        while (true)
        {
            var input = ConsoleUi.Prompt(L.T("cli.tunnels.selectPrompt"), "all")?.ToLowerInvariant();
            if (input == null || input is "q" or "quit" or "exit")
            {
                ConsoleUi.Info(L.T("cli.cancelled"));
                return null;
            }

            if (input is "all" or "a" or "*")
            {
                ConsoleUi.Info(L.Plural("cli.tunnels.selectedAll", tunnels.Count));
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
                ConsoleUi.Warn(L.T("cli.tunnels.notFound", string.Join(", ", invalid)));

            if (selectedIds.Count > 0)
            {
                var names = selectedIds.Select(id => tunnels.First(t => t.Id == id).ProxyName);
                ConsoleUi.Info(L.Plural("cli.tunnels.selected", selectedIds.Count, selectedIds.Count, string.Join(", ", names)));
                return selectedIds;
            }

            ConsoleUi.Warn(L.T("cli.tunnels.noneSelected"));
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

    private static int[] ComputeWidths(IReadOnlyList<string[]> rows, IReadOnlyList<string> headers)
    {
        var widths = new int[headers.Count];
        for (var c = 0; c < headers.Count; c++)
        {
            var content = rows.Count == 0 ? 0 : rows.Max(r => ConsoleUi.DisplayWidth(r[c]));
            widths[c] = Math.Min(MaxWidths[c], Math.Max(ConsoleUi.DisplayWidth(headers[c]), content));
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
        column == HeaderKeys.Length - 1 ? text : ConsoleUi.PadRight(text, width) + new string(' ', ColumnGap);

    private static ConsoleColor TypeColor(string type) => type.ToLowerInvariant() switch
    {
        "tcp" => ConsoleColor.Blue,
        "udp" => ConsoleColor.Magenta,
        "http" or "https" => ConsoleColor.Green,
        "xtcp" or "stcp" or "sudp" => ConsoleColor.Yellow,
        _ => ConsoleColor.Gray
    };
}
