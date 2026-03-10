using System;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using RESPite.Messages;

namespace StackExchange.Redis.Server;

public partial class RedisClient
{
    private static readonly UnboundedChannelOptions s_replyChannelOptions = new()
    {
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false,
    };

    private readonly struct VersionedResponse(TypedRedisValue value, RedisProtocol protocol)
    {
        public readonly TypedRedisValue Value = value;
        public readonly RedisProtocol Protocol = protocol;
    }

    private readonly Channel<VersionedResponse> _replies = Channel.CreateUnbounded<VersionedResponse>(s_replyChannelOptions);

    public void AddOutbound(in TypedRedisValue message)
    {
        if (message.IsNil)
        {
            message.Recycle();
            return;
        }

        try
        {
            var versioned = new VersionedResponse(message, Protocol);
            if (!_replies.Writer.TryWrite(versioned))
            {
                // sorry, we're going to need it, but in reality: we're using
                // unbounded channels, so this isn't an issue
                _replies.Writer.WriteAsync(versioned).AsTask().Wait();
            }
        }
        catch
        {
            message.Recycle();
        }
    }

    public ValueTask AddOutboundAsync(in TypedRedisValue message, CancellationToken cancellationToken = default)
    {
        if (message.IsNil)
        {
            message.Recycle();
            return default;
        }

        try
        {
            var versioned = new VersionedResponse(message, Protocol);
            var pending = _replies.Writer.WriteAsync(versioned, cancellationToken);
            if (!pending.IsCompleted) return Awaited(message, pending);
            pending.GetAwaiter().GetResult();
            // if we succeed, the writer owns it for recycling
        }
        catch
        {
            message.Recycle();
        }
        return default;

        static async ValueTask Awaited(TypedRedisValue message, ValueTask pending)
        {
            try
            {
                await pending;
                // if we succeed, the writer owns it for recycling
            }
            catch
            {
                message.Recycle();
            }
        }
    }

    public void Complete(Exception ex = null) => _replies.Writer.TryComplete(ex);

    public async Task WriteOutputAsync(PipeWriter writer, CancellationToken cancellationToken = default)
    {
        try
        {
            var reader = _replies.Reader;
            do
            {
                int count = 0;
                while (reader.TryRead(out var versioned))
                {
                    WriteResponse(writer, versioned.Value, versioned.Protocol);
                    versioned.Value.Recycle();
                    count++;
                }

                if (count != 0)
                {
#if NET10_0_OR_GREATER
                    Node?.Server?.OnFlush(this, count, writer.CanGetUnflushedBytes ? writer.UnflushedBytes : -1);
#else
                    Node?.Server?.OnFlush(this, count, -1);
#endif
                    await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            // await more data
            while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false));
            await writer.CompleteAsync();
        }
        catch (Exception ex)
        {
            await writer.CompleteAsync(ex);
        }

        static void WriteResponse(IBufferWriter<byte> output, TypedRedisValue value, RedisProtocol protocol)
        {
            static void WritePrefix(IBufferWriter<byte> output, char prefix)
            {
                var span = output.GetSpan(1);
                span[0] = (byte)prefix;
                output.Advance(1);
            }

            if (value.IsNil) return; // not actually a request (i.e. empty/whitespace request)

            var type = value.Type;
            if (protocol is RedisProtocol.Resp2 & type is not RespPrefix.Null)
            {
                if (type is RespPrefix.VerbatimString)
                {
                    var s = (string)value.AsRedisValue();
                    if (s is { Length: >= 4 } && s[3] == ':')
                        value = TypedRedisValue.BulkString(s.Substring(4));
                }
                type = ToResp2(type);
            }
            RetryResp2:
            if (protocol is RedisProtocol.Resp3 && value.IsNullValueOrArray)
            {
                output.Write("_\r\n"u8);
            }
            else
            {
                char prefix;
                switch (type)
                {
                    case RespPrefix.Integer:
                        PhysicalConnection.WriteInteger(output, (long)value.AsRedisValue());
                        break;
                    case RespPrefix.SimpleError:
                        prefix = '-';
                        goto BasicMessage;
                    case RespPrefix.SimpleString:
                        prefix = '+';
                        BasicMessage:
                        WritePrefix(output, prefix);
                        var val = (string)value.AsRedisValue() ?? "";
                        var expectedLength = Encoding.UTF8.GetByteCount(val);
                        PhysicalConnection.WriteRaw(output, val, expectedLength);
                        PhysicalConnection.WriteCrlf(output);
                        break;
                    case RespPrefix.BulkString:
                        PhysicalConnection.WriteBulkString(value.AsRedisValue(), output);
                        break;
                    case RespPrefix.Null:
                    case RespPrefix.Push when value.IsNullArray:
                    case RespPrefix.Map when value.IsNullArray:
                    case RespPrefix.Set when value.IsNullArray:
                    case RespPrefix.Attribute when value.IsNullArray:
                        output.Write("_\r\n"u8);
                        break;
                    case RespPrefix.Array when value.IsNullArray:
                        PhysicalConnection.WriteMultiBulkHeader(output, -1);
                        break;
                    case RespPrefix.Push:
                    case RespPrefix.Map:
                    case RespPrefix.Array:
                    case RespPrefix.Set:
                    case RespPrefix.Attribute:
                        var segment = value.Span;
                        PhysicalConnection.WriteMultiBulkHeader(output, segment.Length, ToResultType(type));
                        foreach (var item in segment)
                        {
                            if (item.IsNil) throw new InvalidOperationException("Array element cannot be nil");
                            WriteResponse(output, item, protocol);
                        }
                        break;
                    default:
                        // retry with RESP2
                        var r2 = ToResp2(type);
                        if (r2 != type)
                        {
                            Debug.WriteLine($"{type} not handled in RESP3; using {r2} instead");
                            goto RetryResp2;
                        }

                        throw new InvalidOperationException(
                            "Unexpected result type: " + value.Type);
                }
            }

            static ResultType ToResultType(RespPrefix type) =>
                type switch
                {
                    RespPrefix.None => ResultType.None,
                    RespPrefix.SimpleString => ResultType.SimpleString,
                    RespPrefix.SimpleError => ResultType.Error,
                    RespPrefix.Integer => ResultType.Integer,
                    RespPrefix.BulkString => ResultType.BulkString,
                    RespPrefix.Array => ResultType.Array,
                    RespPrefix.Null => ResultType.Null,
                    RespPrefix.Boolean => ResultType.Boolean,
                    RespPrefix.Double => ResultType.Double,
                    RespPrefix.BigInteger => ResultType.BigInteger,
                    RespPrefix.BulkError => ResultType.BlobError,
                    RespPrefix.VerbatimString => ResultType.VerbatimString,
                    RespPrefix.Map => ResultType.Map,
                    RespPrefix.Set => ResultType.Set,
                    RespPrefix.Push => ResultType.Push,
                    RespPrefix.Attribute => ResultType.Attribute,
                    // StreamContinuation and StreamTerminator don't have direct ResultType equivalents
                    // These are protocol-level markers, not result types
                    _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unexpected RespPrefix value"),
                };
        }
    }

    public RespPrefix ApplyProtocol(RespPrefix type) => IsResp2 ? ToResp2(type) : type;

    private static RespPrefix ToResp2(RespPrefix type)
    {
        switch (type)
        {
            case RespPrefix.Boolean:
                return RespPrefix.Integer;
            case RespPrefix.Double:
            case RespPrefix.BigInteger:
                return RespPrefix.SimpleString;
            case RespPrefix.BulkError:
                return RespPrefix.SimpleError;
            case RespPrefix.VerbatimString:
                return RespPrefix.BulkString;
            case RespPrefix.Map:
            case RespPrefix.Set:
            case RespPrefix.Push:
            case RespPrefix.Attribute:
                return RespPrefix.Array;
            default: return type;
        }
    }
}
