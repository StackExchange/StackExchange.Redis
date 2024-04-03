using RESPite.Messages;
using System.Buffers;

namespace RESPite.Resp;

// we don't ship these in-box; the idea is that consumers use their own message implementations
public static class RespWriters
{
    /// <summary>
    /// Sets key/value pairs
    /// </summary>
    public static SetWriter Set { get; } = new();

    /// <summary>
    /// Gets key/value pairs
    /// </summary>
    public static GetWriter Get { get; } = new();

    /// <summary>
    /// Sets key/value pairs
    /// </summary>
    public static PingWriter Ping { get; } = new();

    /// <summary>
    /// Sets key/value pairs
    /// </summary>
    public static IncrWriter Incr { get; } = new();

    /// <summary>
    /// Sets key/value pairs
    /// </summary>
    public static DecrWriter Decr { get; } = new();

    /// <summary>
    /// Sets key/value pairs
    /// </summary>
    public sealed class SetWriter : IWriter<(string key, int value)>, IWriter<(string key, byte[] value)>
    {
        internal SetWriter() { }

        void IWriter<(string key, int value)>.Write(in (string key, int value) request, IBufferWriter<byte> target)
        {
            var writer = new RespWriter(target);
            writer.WriteRaw("*3\r\n$3\r\nSET\r\n"u8);
            writer.WriteBulkString(request.key);
            writer.WriteBulkString(request.value);
            writer.Flush();
        }

        void IWriter<(string key, byte[] value)>.Write(in (string key, byte[] value) request, IBufferWriter<byte> target)
        {
            var writer = new RespWriter(target);
            writer.WriteRaw("*3\r\n$3\r\nSET\r\n"u8);
            writer.WriteBulkString(request.key);
            writer.WriteBulkString(request.value);
            writer.Flush();
        }
    }

    /// <summary>
    /// Gets key/value pairs
    /// </summary>
    public sealed class GetWriter : IWriter<string>
    {
        internal GetWriter() { }

        void IWriter<string>.Write(in string key, IBufferWriter<byte> target)
        {
            var writer = new RespWriter(target);
            writer.WriteRaw("*2\r\n$3\r\nGET\r\n"u8);
            writer.WriteBulkString(key);
            writer.Flush();
        }
    }

    public sealed class PingWriter : IWriter<Empty>, IWriter<string>
    {
        internal PingWriter() { }

        void IWriter<string>.Write(in string request, IBufferWriter<byte> target)
        {
            var writer = new RespWriter(target);
            writer.WriteRaw("*2\r\n$4\r\nPING\r\n"u8);
            writer.WriteBulkString(request);
            writer.Flush();
        }

        void IWriter<Empty>.Write(in Empty request, IBufferWriter<byte> target)
            => target.Write("*1\r\nPING\r\n"u8);
    }

    public sealed class IncrWriter : IWriter<string>
    {
        internal IncrWriter() { }

        void IWriter<string>.Write(in string request, IBufferWriter<byte> target)
        {
            var writer = new RespWriter(target);
            writer.WriteRaw("*2\r\n$4\r\nINCR\r\n"u8);
            writer.WriteBulkString(request);
            writer.Flush();
        }
    }

    public sealed class DecrWriter : IWriter<string>
    {
        internal DecrWriter() { }

        void IWriter<string>.Write(in string request, IBufferWriter<byte> target)
        {
            var writer = new RespWriter(target);
            writer.WriteRaw("*2\r\n$4\r\nDECR\r\n"u8);
            writer.WriteBulkString(request);
            writer.Flush();
        }
    }

}
