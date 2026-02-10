using System.Text.Json;
using Kairo.Core.Daemon;

namespace Kairo.Daemon;

/// <summary>
/// Daemon 持久化状态管理 — 用于 daemon 崩溃后恢复 frpc 进程
/// </summary>
internal sealed class StateManager
{
    private readonly string _stateFilePath;
    private readonly object _fileLock = new();
    private Timer _autoSaveTimer;

    public StateManager()
    {
        _stateFilePath = DaemonConstants.GetStateFilePath();
        // 每 5 秒自动保存一次状态
        _autoSaveTimer = new Timer(_ => { }, null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// 启动自动保存
    /// </summary>
    public void StartAutoSave(Func<DaemonState> stateProvider)
    {
        _autoSaveTimer.Change(0, 5000);
        _autoSaveTimer.Dispose();
        _autoSaveTimer = new Timer(_ =>
        {
            try
            {
                var state = stateProvider();
                Save(state);
            }
            catch { }
        }, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// 保存状态到文件
    /// </summary>
    public void Save(DaemonState state)
    {
        lock (_fileLock)
        {
            try
            {
                var dir = Path.GetDirectoryName(_stateFilePath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(state, DaemonJsonContext.Default.DaemonState);

                // 原子写入：先写临时文件再重命名
                var tempPath = _stateFilePath + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, _stateFilePath, overwrite: true);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[state-manager] 保存状态失败: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 加载状态
    /// </summary>
    public DaemonState? Load()
    {
        lock (_fileLock)
        {
            try
            {
                if (!File.Exists(_stateFilePath)) return null;
                var json = File.ReadAllText(_stateFilePath);
                return JsonSerializer.Deserialize(json, DaemonJsonContext.Default.DaemonState);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[state-manager] 加载状态失败: {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// 删除状态文件
    /// </summary>
    public void Delete()
    {
        lock (_fileLock)
        {
            try { File.Delete(_stateFilePath); } catch { }
        }
    }

    /// <summary>
    /// 停止自动保存
    /// </summary>
    public void StopAutoSave()
    {
        _autoSaveTimer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    public void Dispose()
    {
        StopAutoSave();
        _autoSaveTimer.Dispose();
    }
}
