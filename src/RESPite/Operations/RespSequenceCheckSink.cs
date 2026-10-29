using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks.Sources;
using RESPite.Buffers;

namespace RESPite.Operations;

/// <summary>
/// The pending-queue entry that stands in for a sequence check's reply (see
/// <see cref="RespConnection.SequenceCheckHead"/>): a marker, so the reader loop can recognise the frame
/// that answers a check and validate it, rather than hand it to the next operation.
/// </summary>
/// <remarks>
/// <para>
/// <b>One per connection, enqueued once per check written</b>, exactly as <see cref="RespPrefixReplySink"/>
/// is for a prefix. It holds no state of its own: the token a check must carry is the connection's running
/// counter, issued under the write lock in wire order and consumed on the reader in the same order, so "the
/// <i>n</i>th check answers with <i>n</i>" is the whole of the validation and nothing need be stored per entry.
/// </para>
/// <para>
/// Unlike a prefix reply, this frame is never discarded quietly. The connection validates it itself (see
/// <c>RespConnection.Drain</c>); this type only exists so that the entry has an identity the loop can test
/// for, and so that <c>Close</c> can fail everything queued without special-casing it.
/// </para>
/// </remarks>
internal sealed class RespSequenceCheckSink : IRespMessage
{
    short IRespMessage.Token => 0;

    bool IRespMessage.AllowInlineParsing => true;

    bool IRespMessage.IsFinished => true; // nothing is ever owed to anybody for it

    byte[]? IRespMessage.Prefix => null;

    bool IRespMessage.SequenceChecked => false;

    void IRespMessage.OnSequenceCheck(uint token)
    {
    }

    void IRespMessage.Wait(short token, TimeSpan timeout) => throw NotWaitable();

    bool IRespMessage.TryReserveRequest(short token, out ReadOnlyMemory<byte> payload, bool recordSent)
    {
        payload = default;
        return false; // it is never written by a run; the connection writes the check itself
    }

    void IRespMessage.ReleaseRequest()
    {
    }

    void IRespMessage.OnEnqueued(object connection, long bytesSent, long bytesReceived)
    {
    }

    // the connection validates the frame before it would reach here; see RespConnection.Drain
    bool IRespMessage.TrySetResult(short token, scoped ReadOnlySpan<byte> response, IPayloadReservationProvider? source) => true;

    bool IRespMessage.TrySetResult(short token, in ReadOnlySequence<byte> response) => true;

    // the connection is closing, or an operation is being timed out: a check has nobody to tell
    bool IRespMessage.TrySetException(short token, Exception exception, bool definite) => false;

    bool IRespMessage.TrySetCanceled(short token, CancellationToken cancellationToken) => false;

    void IRespMessage.TrySetCanceled()
    {
    }

    void IRespMessage.TrySetTimedOut()
    {
    }

    bool IRespMessage.TryTimeoutIfOlderThan(short token, TimeSpan age) => false;

    void IValueTaskSource.GetResult(short token) => throw NotWaitable();

    ValueTaskSourceStatus IValueTaskSource.GetStatus(short token) => ValueTaskSourceStatus.Succeeded;

    void IValueTaskSource.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
        => throw NotWaitable();

    private static InvalidOperationException NotWaitable()
        => new("A sequence check's reply is validated by the connection; nothing can wait on it.");
}
