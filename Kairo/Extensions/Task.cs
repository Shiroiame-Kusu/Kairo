using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Kairo.Extensions
{
    internal static partial class Extensions
    {
        /// <summary>
        /// 等待
        /// </summary>
        /// <param name="task">任务</param>
        /// <returns>等待结果</returns>
        public static T Await<T>(this Task<T> task)
        {
            return task.GetAwaiter().GetResult();
        }
    }
}
