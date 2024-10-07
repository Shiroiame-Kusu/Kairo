using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LoCyanFrpDesktop.Utils
{
    public class ScheduledTask
    {   
        private static List<Action> ScheduledActions = new List<Action>();
        public static void Init()
        {
            ScheduledActions.Add(new Action(() =>
            {
                while (true)
                {
                    using (ConfigManager ConfigManager = new(FileMode.Create))
                    {
                        Console.WriteLine("Saving...");
                    }
                    Thread.Sleep(1000);
                }
            }));
            for (int i = 0; i < ScheduledActions.Count; i++)
            {
                Run(ScheduledActions[i]);
            }
        }
        private static async void Run(Action action) {
                Task.Run(() => { 
                    action();
                });
        }
        public static void Add(Action action) { 
            ScheduledActions.Add(action);
            Run(action);
        }
    }
}
