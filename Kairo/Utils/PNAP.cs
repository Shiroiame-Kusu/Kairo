using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Kairo.Utils
{   
    public class PNAP
    {   

        public static List<PNAPListComp>? PNAPList { get; set; }
        public class PNAPListComp
        {
            public int? ProcessName { get; set; }
            public bool? IsRunning { get; set; }
            public int Pid { get; set; }
            public int? ListIndex { get; set; }
            public override string ToString()
            {
                return ProcessName.ToString();
            }
        }
        
    }

    
}
