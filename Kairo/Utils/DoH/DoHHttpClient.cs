using Kairo.Utils.DoH;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Kairo.Utils.DoH
{
    public class DoHHttpClient : System.Net.Http.HttpClient
    {   
        
        public DoHHttpClient()
            : base(new DnsOverHttpsHandler())
        {
        }
    }
}
