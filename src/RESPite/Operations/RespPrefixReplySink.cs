using System;
using System.Buffers;
using System.Text;
using System.Threading;
using System.Threading.Tasks.Sources;
using RESPite.Buffers;

namespace RESPite.Operations;

/// <summary>
/// The pending-queue entry that stands in for a prefix's reply (see <see cref="IRespMessage.Prefix"/>):
/// it takes the frame, counts it if it was an error, and completes nothing.
/// </summary>
/// <remarks>
/// <para>
/// <b>One per connection, enqueued once per prefix written.</b> It never changes state, so one instance
/// can sit in the queue any number of times at once; its token is a constant, so every entry naming it is
/// "current" and takes exactly one frame. That is the whole mechanism by which a prefix costs the caller
/// nothing: the frame that answers it has somewhere to go that is not the next operation.
/// </para>
/// <para>
/// An error reply is the case worth recording. <c>CLIENT CACHING YES</c> on a connection whose tracking
/// mode does not allow it answers <c>-ERR</c>; the command behind it still runs and still replies, so
/// nothing fails - but the modifier was not applied, and "why is this not being tracked?" should have an
/// answer. <see cref="Errors"/> and <see cref="LastError"/> are that answer; the request itself is not
/// faulted, because degrading to "uncached" is strictly better than failing a read that succeeded.
/// </para>
/// </remarks>
internal sealed class RespPrefixReplySink : IRespMessage
{
    private long _errors;
    private string? _lastError;

    /// <summary>How many prefix replies were errors.</summary>
    public long Errors => Volatile.Read(ref _errors);

    /// <summary>The most recent error reply, as text, or null.</summary>
    public string? LastError => Volatile.Read(ref _lastError);

    short IRespMessage.Token => 0;

    bool IRespMessage.AllowInlineParsing => true;

    bool IRespMessage.IsFinished => true; // nothing is ever owed to anybody for it

    byte[]? IRespMessage.Prefix => null;

    bool IRespMessage.SequenceChecked => false; // it is never written, so there is nothing to check

    void IRespMessage.OnSequenceCheck(uint token)
    {
    }

    void IRespMessage.Wait(short token, TimeSpan timeout) => throw NotWaitable();

    bool IRespMessage.TryReserveRequest(short token, out ReadOnlyMemory<byte> payload, bool recordSent)
    {
        payload = default;
        return false; // it is never written; it only receives
    }

    void IRespMessage.ReleaseRequest()
    {
    }

    void IRespMessage.OnEnqueued(object connection, long bytesSent, long bytesReceived)
    {
    }

    bool IRespMessage.TrySetResult(short token, scoped ReadOnlySpan<byte> response, IPayloadReservationProvider? source)
    {
        Note(response);
        return true;
    }

    bool IRespMessage.TrySetResult(short token, in ReadOnlySequence<byte> response)
    {
        if (response.IsSingleSegment)
        {
            Note(response.First.Span);
        }
        else if (!response.IsEmpty)
        {
            // an error reply is short; a multi-segment one is almost certainly not an error, but check the
            // first byte rather than assume
            Span<byte> first = stackalloc byte[1];
            response.Slice(0, 1).CopyTo(first);
            if (first[0] == (byte)'-') Note(response.ToArray());
        }

        return true;
    }

    private void Note(ReadOnlySpan<byte> response)
    {
        if (response.IsEmpty || response[0] != (byte)'-') return;

        // strip the leading '-' and the trailing CRLF; the text is for a human
        var text = response.Slice(1);
        if (text.Length >= 2 && text[text.Length - 2] == (byte)'\r') text = text.Slice(0, text.Length - 2);
        Volatile.Write(ref _lastError, Encoding.UTF8.GetString(text.ToArray()));
        Interlocked.Increment(ref _errors);
    }

    // the connection is closing, or an operation is being timed out: a prefix has nobody to tell
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
        => new("A prefix reply is discarded by the connection; nothing can wait on it.");
}
