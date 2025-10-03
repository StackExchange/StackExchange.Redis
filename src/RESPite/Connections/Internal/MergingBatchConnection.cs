using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RESPite.Internal;
using RESPite.Messages;

namespace RESPite.Connections.Internal;

/// <summary>
/// Holds basic RespOperation, queue and release - turns
/// multiple send calls into a single multi-message send.
/// </summary>
internal sealed class MergingBatchConnection(in RespContext context, int sizeHint) : BufferingBatchConnection(context, sizeHint)
{
    // Collate new messages in a batch-specific buffer, rather than the usual thread-local one; this means
    // that all the messages will be in contiguous memory.
    private readonly BlockBufferSerializer _serializer = BlockBufferSerializer.Create(retainChain: true);

    protected override void OnDispose(bool disposing)
    {
        if (disposing)
        {
            _serializer.Clear();
        }

        base.OnDispose(disposing);
    }

    internal override BlockBufferSerializer Serializer
    {
        get
        {
            ThrowIfDisposed();
            return _serializer;
        }
    }

    private bool Flush(out RespOperation single)
    {
        lock (SyncLock)
        {
            var payload = _serializer.Flush();
            var count = Flush(out var oversized, out single);
            switch (count)
            {
                case 0:
                    Debug.Assert(payload.IsEmpty);
                    return false;
                case 1:
                    Debug.Assert(!payload.IsEmpty);
                    // send as a single-message we don't need the extra add-ref on the entire payload
                    BlockBufferSerializer.BlockBuffer.Release(in payload);

                    return true;
                default:
                    Debug.Assert(!payload.IsEmpty);
                    var msg = RespMultiMessage.Get(oversized, count);
                    msg.Init(payload, Context.CancellationToken);
                    single = new(msg);
                    return true;
            }
        }
    }

    public override Task FlushAsync()
    {
        return Flush(out var single)
            ? Tail.WriteAsync(single)
            : Task.CompletedTask;
    }

    public override void Flush()
    {
        if (Flush(out var single))
        {
            Tail.Write(single);
        }
    }
}
