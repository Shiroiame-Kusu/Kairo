using Kairo.Properties;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Kairo.Components
{
    internal class BuildInfo
    {
        public BuildInfo()
        {
            string[] arg = System.Text.RegularExpressions.Regex.Replace(
                (Resources.BuildInfo ?? string.Empty).Trim(' ', '\n', '\r').Replace("\r", string.Empty),
                @"[^\u0000-\u007f]+", string.Empty).Split(new[] { '\n' }, 3, StringSplitOptions.RemoveEmptyEntries);
            foreach (string arg0 in arg)
            {
             Console.WriteLine(arg0);   
            }
            switch (arg.Length)
            {
                case 1:
                    Type = arg[0].Trim();
                    break;
                case 2:
                    Type = arg[0].Trim();
                    Time = arg[1].Trim();
                    break;
                case 3:
                    Type = arg[0].Trim();
                    Time = arg[1].Trim();
                    Detail = arg[2].Trim().Replace("\\n", "\n");
                    break;
                default:
                    break;
            }
        }

        public override string ToString()
        {
            return "" +
                   $"编译类型：{Type}\r\n" +
                   $"编译时间：{Time}\r\n" +
                   $"详细信息：{Detail}\r\n" +
                   $"当前分支：{Global.Branch}";
        }

        /// <summary>
        /// 编译类型
        /// </summary>
        public string Type { get; private set; } = "未知";

        /// <summary>
        /// 编译时间
        /// </summary>
        public string Time
        {
            get => string.IsNullOrEmpty(_time) ? "-" : _time!;
            set => _time = value;
        }
        private string? _time;

        /// <summary>
        /// 详细信息
        /// </summary>
        public string Detail
        {
            get => string.IsNullOrEmpty(_detail) ? "-" : _detail!;
            set => _detail = value;
        }

        private string? _detail;
    }
}