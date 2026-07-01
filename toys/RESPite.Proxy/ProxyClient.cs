using System.Buffers;
using System.IO.Pipelines;
using RESPite.Messages;
using RESPite.Streams;

namespace RESPite.Proxy;

internal sealed class ProxyClient(ProxyServer.InnerLeg upstream, Stream inbound, PipeWriter outbound)
    : RespStream(inbound)
{
    public int Id { get; set; }
    public int Database => _db;
    private int _db;
    private TaskCompletionSource _completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private bool OnSelect(int db)
    {
        if (db < 0 | db > 999_999_999) return false;
        _db = db;
        return true;
    }

    public Task ExecuteAsync()
    {
        StartReading(sync: false, cancellationToken: upstream.Lifetime);
        return _completionSource.Task;
    }

    private protected override void RecordConnectionFailed(StreamFailureKind kind, Exception? fault = null)
    {
        if (fault is null)
        {
            _completionSource.TrySetResult();
        }
        else
        {
            _completionSource.TrySetException(fault);
        }

        upstream.Remove(this);
    }

    protected override void OnReadFrame(RespPrefix prefix, ReadOnlySpan<byte> frame, ref IMemoryOwner<byte>? memoryOwner)
    {
        var reader = new RespReader(frame);
#pragma warning disable CS0618
        if ((reader.TryReadNext() & reader.Prefix is RespPrefix.Array)
            && (reader.TryReadNext() & reader.Prefix is RespPrefix.BulkString))
#pragma warning restore CS0618
        {
            KnownCommands command;
            unsafe
            {
                if (!reader.TryParseScalar(&KnownCommandsMetadata.TryParse, out command))
                    command = KnownCommands.Unknown;
            }

            switch (command)
            {
                case KnownCommands.Unknown:
                    SendResponse("-ERR unknown command\r\n"u8); // we should probably tell them what, but... meh
                    break;
                case KnownCommands.Select:
#pragma warning disable CS0618
                    if (reader.TryReadNext() & reader.Prefix is RespPrefix.BulkString & reader.TryReadInt32(out var db)
                        & !reader.TryReadNext() && OnSelect(db))
#pragma warning restore CS0618
                    {
                        SendResponse("+OK\r\n"u8);
                    }
                    else
                    {
                        SendResponse("-ERR invalid SELECT syntax\r\n"u8);
                    }
                    break;
                default:
                    // known, and no special rules; forward it
                    upstream.Send(_db, this, frame);
                    break;
            }
        }
        else
        {
            SendResponse("-ERR invalid request\r\n"u8);
        }
    }

    public void SendResponse(ReadOnlySpan<byte> frame)
    {
        outbound.Write(frame);
        var vt = outbound.FlushAsync(upstream.Lifetime);
        if (vt.IsCompletedSuccessfully)
        {
            _ = vt.Result;
        }
        else
        {
            vt.AsTask().Wait(); // for test only
        }
    }
}

internal static partial class KnownCommandsMetadata
{
    [AsciiHash(CaseSensitive = false)]
    public static partial bool TryParse(ReadOnlySpan<byte> data, out KnownCommands command);
}

internal enum KnownCommands
{
    Unknown = 0,
    Get,
    Set,
    Select,
}
