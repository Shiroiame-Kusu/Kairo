using System;
using System.Diagnostics;
using System.IO;
using Kairo.Core;
using Kairo.Utils.Configuration;
using Kairo.Utils.Logger;
using AppLogger = Kairo.Utils.Logger.Logger;

namespace Kairo.Utils;

/// <summary>
/// Helper class for launching the Updater and exiting the application.
/// Handles cross-platform concerns (Windows vs Linux/macOS).
/// </summary>
public static class UpdaterHelper
{
    private static ProcessStartInfo? _pendingUpdater;

    /// <summary>
    /// Prepares the updater to be launched when the application exits.
    /// Call this before initiating shutdown.
    /// </summary>
    /// <param name="targetVersion">The version to update to</param>
    /// <returns>True if updater was found and prepared, false otherwise</returns>
    public static bool PrepareUpdate(AppVersion targetVersion)
    {
        var baseDir = AppContext.BaseDirectory;
        var updaterExe = Path.Combine(baseDir, OperatingSystem.IsWindows() ? "Updater.exe" : "Updater");
        
        if (!File.Exists(updaterExe))
        {
            _pendingUpdater = null;
            return false;
        }

        var currentPid = Process.GetCurrentProcess().Id;
        var channelArg = targetVersion.ChannelName;

        _pendingUpdater = new ProcessStartInfo(updaterExe)
        {
            UseShellExecute = false,
            WorkingDirectory = baseDir,
            Arguments = $"{currentPid} Shiroiame-Kusu Kairo {channelArg}"
        };
        return true;
    }

    /// <summary>
    /// Prepares the updater using channel name directly.
    /// </summary>
    public static bool PrepareUpdate(string channelName)
    {
        var baseDir = AppContext.BaseDirectory;
        var updaterExe = Path.Combine(baseDir, OperatingSystem.IsWindows() ? "Updater.exe" : "Updater");
        
        if (!File.Exists(updaterExe))
        {
            _pendingUpdater = null;
            return false;
        }

        var currentPid = Process.GetCurrentProcess().Id;

        _pendingUpdater = new ProcessStartInfo(updaterExe)
        {
            UseShellExecute = false,
            WorkingDirectory = baseDir,
            Arguments = $"{currentPid} Shiroiame-Kusu Kairo {channelName}"
        };
        return true;
    }

    /// <summary>
    /// Checks if an updater component exists.
    /// </summary>
    public static bool IsUpdaterAvailable()
    {
        var baseDir = AppContext.BaseDirectory;
        var updaterExe = Path.Combine(baseDir, OperatingSystem.IsWindows() ? "Updater.exe" : "Updater");
        return File.Exists(updaterExe);
    }

    /// <summary>
    /// Launches the updater and exits the application.
    /// On Windows, this uses a special exit sequence to ensure proper process termination.
    /// </summary>
    public static void LaunchUpdaterAndExit()
    {
        if (_pendingUpdater == null)
            throw new InvalidOperationException("No updater prepared. Call PrepareUpdate first.");

        try
        {
            // Start the updater process
            Process.Start(_pendingUpdater);
        }
        catch (Exception ex)
        {
            AppLogger.Output(LogType.Error, "[Updater] Failed to start updater:", ex.Message);
            throw;
        }

        // Stop all frpc processes before exiting
        try { FrpcProcessManager.StopAll(); } catch { }
        
        // Flush any pending I/O
        try { ConfigManager.Save(); } catch { }

        // Exit the application
        Environment.Exit(0);
    }

    /// <summary>
    /// Launches the updater and returns immediately (for async scenarios).
    /// The caller is responsible for shutting down the application.
    /// </summary>
    /// <returns>True if updater was started successfully</returns>
    public static bool LaunchUpdater()
    {
        if (_pendingUpdater == null)
            return false;

        try
        {
            Process.Start(_pendingUpdater);
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Output(LogType.Error, "[Updater] Failed to start updater:", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Resets the pending updater state.
    /// </summary>
    public static void ClearPendingUpdate()
    {
        _pendingUpdater = null;
    }

    /// <summary>
    /// Gets whether an update is pending launch.
    /// </summary>
    public static bool HasPendingUpdate => _pendingUpdater != null;
}
