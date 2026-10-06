using Kairo.Properties;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Kairo.Core;
using Kairo.Core.Localization;

namespace Kairo.Components
{
    internal class BuildInfo
    {
        public BuildInfo()
        {
            string[] arg = Regex.Replace(
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
            return L.T("buildInfo.type", Type) + "\r\n" +
                   L.T("buildInfo.time", Time) + "\r\n" +
                   L.T("buildInfo.detail", Detail) + "\r\n" +
                   L.T("buildInfo.branch", Global.Branch.ToDisplayName());
        }

        /// <summary>
        /// 编译类型
        /// </summary>
        public string Type
        {
            get => string.IsNullOrEmpty(_type) ? L.T("common.unknown") : _type!;
            private set => _type = value;
        }
        private string? _type;

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