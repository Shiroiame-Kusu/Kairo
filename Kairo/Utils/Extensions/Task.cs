using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Kairo.Utils.Extensions
{
    internal static partial class Extensions
    {
        public static T Await<T>(this Task<T> task)
        {
            return task.GetAwaiter().GetResult();
        }
    }
}