using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Kairo.Utils.Logger;

namespace Kairo.Utils
{
    internal static class DesignModeHelper
    {
        public static bool IsDesign => Design.IsDesignMode;

        public static void SafeRuntime(Action runtimeAction, Action? designFallback = null)
        {
            if (IsDesign)
            {
                designFallback?.Invoke();
                return;
            }
            runtimeAction();
        }

        public static IEnumerable<(LogType type, string line)> SampleLogs => new List<(LogType, string)>
        {
            (LogType.Info, "[I] 应用已加载 (设计时示例)"),
            (LogType.Warn, "[W] 示例警告: 配置文件缺失, 使用默认值"),
            (LogType.Error, "[E] 示例错误: 无法连接到服务器"),
            (LogType.Debug, "[DEBUG] 调试输出 -> value=null flag=true"),
            (LogType.DetailDebug, "[TRACE] 深度调试: iteration=42 timing=12ms"),
            (LogType.Info, "[I] 127.0.0.1:7000 -> Node1:6000 映射建立")
        };
    }
}

