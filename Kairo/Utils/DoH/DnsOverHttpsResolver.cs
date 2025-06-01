using DnsClient;
using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.DirectoryServices.ActiveDirectory;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Kairo.Utils.DoH
{
    public class DnsOverHttpsResolver
    {
        private readonly System.Net.Http.HttpClient _dohClient;
        private readonly string _dohServerUrl;

        public DnsOverHttpsResolver(string dohServerUrl = "https://dns.alidns.com/dns-query")
        {
            _dohClient = new System.Net.Http.HttpClient();
            _dohServerUrl = dohServerUrl;
        }
        byte[] BuildDnsQuery(string domain)
        {
            Random rand = new Random();
            ushort id = (ushort)rand.Next(0, 65536); // 随机请求 ID

            byte[] header = new byte[12];
            header[0] = (byte)(id >> 8); // ID
            header[1] = (byte)(id & 0xFF);
            header[2] = 0x01; // 标准查询
            header[3] = 0x00;
            header[4] = 0x00; header[5] = 0x01; // QDCOUNT = 1
            header[6] = 0x00; header[7] = 0x00; // ANCOUNT = 0
            header[8] = 0x00; header[9] = 0x00; // NSCOUNT = 0
            header[10] = 0x00; header[11] = 0x00; // ARCOUNT = 0

            byte[] question = BuildQuestionSection(domain);

            // 拼接 header + question + QTYPE + QCLASS
            byte[] query = new byte[header.Length + question.Length + 4];
            Buffer.BlockCopy(header, 0, query, 0, header.Length);
            Buffer.BlockCopy(question, 0, query, header.Length, question.Length);

            // QTYPE = A (0x0001)
            query[header.Length + question.Length + 0] = 0x00;
            query[header.Length + question.Length + 1] = 0x01;
            // QCLASS = IN (0x0001)
            query[header.Length + question.Length + 2] = 0x00;
            query[header.Length + question.Length + 3] = 0x01;

            return query;
        }

        // 构造 Question 部分（域名编码为 DNS 格式）
        byte[] BuildQuestionSection(string domain)
        {
            string[] labels = domain.Split('.');
            int length = domain.Length + 2 + 1; // 每个 label 多一个长度字节，结尾加 0
            byte[] result = new byte[length];
            int pos = 0;

            foreach (var label in labels)
            {
                result[pos++] = (byte)label.Length;
                byte[] labelBytes = Encoding.ASCII.GetBytes(label);
                Buffer.BlockCopy(labelBytes, 0, result, pos, labelBytes.Length);
                pos += labelBytes.Length;
            }

            result[pos] = 0x00; // 结尾的0表示域名结束

            return result;
        }
        public async Task<IPAddress> ResolveAsync(string hostname)
        {
            throw new NotImplementedException();
        }
        /*
        public async Task<IPAddress> ResolveAsync(string hostname)
        {

            byte[] query = BuildDnsQuery(hostname);


            // 转换为wire格式并编码
            HttpContent content = new ByteArrayContent(query);
            // 发送DoH请求
            _dohClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/dns-message"));
            var url = $"{_dohServerUrl}?dns={Convert.ToBase64String(Encoding.UTF8.GetBytes(hostname)).Replace("=","")}&type=A";
            Console.WriteLine(url);
            var response = await _dohClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var answers = doc.RootElement.GetProperty("Answer");

            foreach (var answer in answers.EnumerateArray())
            {
                if (answer.TryGetProperty("data", out var data))
                {
                    var ipString = data.GetString();
                    if (IPAddress.TryParse(ipString, out var ipAddress))
                    {
                        return ipAddress;
                    }
                }
            }
            throw new Exception($"No valid IP address found for {hostname}");
        }*/
    }
}
