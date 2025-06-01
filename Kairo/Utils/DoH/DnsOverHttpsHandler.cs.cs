using Kairo.Utils.DoH;
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public class DnsOverHttpsHandler : DelegatingHandler
{
    private readonly DnsOverHttpsResolver _resolver;

    public DnsOverHttpsHandler()
    {
        _resolver = new DnsOverHttpsResolver();
        InnerHandler = new SocketsHttpHandler(); // 使用默认 SocketsHttpHandler
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // 解析原始主机名
        var originalHost = request.RequestUri!.Host;
        var ipAddress = await _resolver.ResolveAsync(originalHost);

        // 替换 URI 中的主机名为 IP，保留原始端口
        var builder = new UriBuilder(request.RequestUri)
        {
            Host = ipAddress.ToString(),
            Port = request.RequestUri.Port
        };
        request.RequestUri = builder.Uri;

        // 设置 Host 头为原始域名，确保 TLS 证书验证通过
        request.Headers.Host ??= originalHost;

        return await base.SendAsync(request, cancellationToken);
    }
}