using System;
using System.Diagnostics;
using RESPite.Buffers;
using RESPite.Messages;

namespace StackExchange.Redis;

/// <summary>
/// Reads <see cref="RespReader"/> payloads as <see cref="ReadOnlyLease{T}"/>, sharing the underlying
/// memory where that is possible.
/// </summary>
/// <remarks>
/// <para>
/// A separate class from <see cref="RespReaderExtensions"/> on purpose. The method it replaces keeps its
/// name, its signature and its containing type - so anything already compiled against it still binds - and
/// merely stops being an extension method. <c>reader.ReadLease()</c> therefore resolves here instead,
/// while <c>RespReaderExtensions.ReadLease(reader)</c> still reaches the old one for anybody who wants a
/// buffer they own outright.
/// </para>
/// <para>
/// That is a <b>source</b> break for callers who named the type (<c>Lease&lt;byte&gt; x = ...</c>) or wrote
/// through it, and deliberately so: those are exactly the callers for whom sharing would have been unsafe,
/// and a compile error is how they find out. It is not a binary break; a rebuild on upgrade picks up the
/// copy-safe version.
/// </para>
/// </remarks>
public static class RespReaderLeaseExtensions
{
    /// <summary>
    /// Read a scalar value as a <see cref="ReadOnlyLease{T}"/>, sharing the reader's buffer when the
    /// payload is one contiguous run inside a buffer that supports counted reservations.
    /// </summary>
    /// <param name="reader">The reader to read from.</param>
    /// <returns>The payload, or <c>null</c> for a RESP null.</returns>
    /// <remarks>
    /// <para>
    /// Sharing is safe here for a reason the mutable <see cref="Lease{T}"/> cannot offer: nothing can write
    /// through this lease, so a second holder of the same memory cannot be surprised by it.
    /// </para>
    /// <para>
    /// Sharing does <b>pin</b> - the buffer stays alive until the lease is disposed, so a small payload can
    /// hold a large reply. That cost is real for a reply that would otherwise be recycled immediately, and
    /// absent for a cached one, which is held for its own lifetime regardless. Use
    /// <see cref="ReadOnlyLease{T}.ToArray"/> when a small value has to outlive a large reply.
    /// </para>
    /// <para>
    /// Copies when it must: a streamed scalar arrives in chunks and has to be assembled before it can be a
    /// single contiguous run.
    /// </para>
    /// </remarks>
    public static ReadOnlyLease<byte>? ReadLease(this in RespReader reader)
    {
        reader.DemandScalar();
        if (reader.IsNull) return null;

        var length = reader.ScalarLength();
        if (length == 0) return ReadOnlyLease<byte>.Empty;

        if (reader.TryReservePayload(out var reservation))
        {
            Debug.Assert(reservation.Length == length, "reserved length mismatch");
            return ReadOnlyLease<byte>.Share(reservation.Owner, reservation.Offset, reservation.Length);
        }

        // no reservation available - a chunked payload, or a buffer that does not support counted
        // reservations - so assemble a copy, renting from the pool the data itself came from
        reader.TryGetService<IBufferPoolProvider>(out var pools);
        var lease = ReadOnlyLease<byte>.Rent(length, pools?.BufferPool, out var target);
        if (reader.TryGetSpan(out var span))
        {
            span.CopyTo(target);
        }
        else
        {
            var buffer = reader.Buffer(target);
            Debug.Assert(buffer.Length == length, "buffer length mismatch");
        }

        return lease;
    }
}
