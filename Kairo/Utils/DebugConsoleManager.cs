using System;
using System.Runtime.InteropServices;

namespace Kairo.Utils;

internal static class DebugConsoleManager
{
    private static bool _consoleAllocated;

    public static void Sync(bool enabled)
    {
        if (!OperatingSystem.IsWindows())
            return;

        if (enabled)
            EnsureConsole();
        else
            ReleaseConsole();
    }

    private static void EnsureConsole()
    {
        if (_consoleAllocated)
            return;

        if (AttachConsole(ATTACH_PARENT_PROCESS) || AllocConsole())
        {
            _consoleAllocated = true;
            try { Console.Title = "Kairo Debug Console"; } catch { }
        }
    }

    private static void ReleaseConsole()
    {
        if (!_consoleAllocated)
            return;

        FreeConsole();
        _consoleAllocated = false;
    }

    private const int ATTACH_PARENT_PROCESS = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);
}

