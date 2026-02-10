using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Kairo.Core.Daemon;

/// <summary>
/// IPC 消息帧协议
/// 帧格式: [4 bytes big-endian length] [1 byte message kind] [payload UTF-8 JSON]
/// </summary>
public static class IpcProtocol
{
    /// <summary>消息类别标记</summary>
    public enum MessageKind : byte
    {
        Request = 0x01,
        Response = 0x02,
        Event = 0x03
    }

    /// <summary>
    /// 向 socket 写入一帧消息
    /// </summary>
    public static async ValueTask WriteFrameAsync<T>(
        NetworkStream stream,
        MessageKind kind,
        T message,
        JsonTypeInfo<T> typeInfo,
        CancellationToken ct = default)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(message, typeInfo);
        var totalLen = 1 + json.Length; // 1 byte kind + payload

        if (totalLen > DaemonConstants.MaxMessageSize)
            throw new InvalidOperationException($"Message too large: {totalLen} bytes");

        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, totalLen);

        await stream.WriteAsync(header, ct).ConfigureAwait(false);
        stream.WriteByte((byte)kind);
        await stream.WriteAsync(json, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 从 socket 读取一帧消息
    /// </summary>
    /// <returns>消息类别和原始 JSON 字节；消息结束返回 null</returns>
    public static async ValueTask<(MessageKind Kind, byte[] Payload)?> ReadFrameAsync(
        NetworkStream stream,
        CancellationToken ct = default)
    {
        var header = new byte[4];
        var headerRead = await ReadExactAsync(stream, header, 0, 4, ct).ConfigureAwait(false);
        if (headerRead < 4) return null; // stream ended

        var totalLen = BinaryPrimitives.ReadInt32BigEndian(header);
        if (totalLen <= 0 || totalLen > DaemonConstants.MaxMessageSize)
            throw new InvalidOperationException($"Invalid frame length: {totalLen}");

        var frame = new byte[totalLen];
        var frameRead = await ReadExactAsync(stream, frame, 0, totalLen, ct).ConfigureAwait(false);
        if (frameRead < totalLen) return null; // stream ended

        var kind = (MessageKind)frame[0];
        var payload = new byte[totalLen - 1];
        Buffer.BlockCopy(frame, 1, payload, 0, totalLen - 1);

        return (kind, payload);
    }

    /// <summary>
    /// 反序列化消息
    /// </summary>
    public static T Deserialize<T>(byte[] payload, JsonTypeInfo<T> typeInfo)
        => JsonSerializer.Deserialize(payload, typeInfo)
           ?? throw new InvalidOperationException("Failed to deserialize IPC message");

    /// <summary>
    /// 确保读到足够字节
    /// </summary>
    private static async ValueTask<int> ReadExactAsync(
        NetworkStream stream, byte[] buffer, int offset, int count, CancellationToken ct)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            int read = await stream.ReadAsync(
                buffer.AsMemory(offset + totalRead, count - totalRead), ct).ConfigureAwait(false);
            if (read == 0) return totalRead; // EOF
            totalRead += read;
        }
        return totalRead;
    }
}

// ─── AOT-compatible JSON source generators ────────────────────────

[JsonSerializable(typeof(IpcRequest))]
[JsonSerializable(typeof(IpcResponse))]
[JsonSerializable(typeof(IpcEvent))]
[JsonSerializable(typeof(DaemonState))]
[JsonSerializable(typeof(ProxyState))]
[JsonSerializable(typeof(LogLine))]
[JsonSerializable(typeof(List<ProxyState>))]
[JsonSerializable(typeof(List<LogLine>))]
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public partial class DaemonJsonContext : JsonSerializerContext
{
}
