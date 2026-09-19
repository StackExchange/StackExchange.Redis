using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks.Sources;

namespace RESPite.Operations;

/// <summary>
/// The untyped view of an operation: everything the pipeline needs in order to write a request and
/// deliver an outcome, without knowing what the reply parses into.
/// </summary>
/// <remarks>
/// <para>
/// This is the seam that lets the connection layer be non-generic. A connection holds
/// <see cref="IRespMessage"/>, writes <see cref="TryReserveRequest"/>'s bytes, and hands the reply back
/// through <c>TrySetResult</c>; the typed half - parsing, and the <c>ValueTask&lt;T&gt;</c> the caller
/// awaits - lives in <c>RespMessageBase&lt;T&gt;</c> above it and never reaches the queue.
/// </para>
/// <para>
/// <b>Every mutator takes a token.</b> Once instances are pooled, a holder of a stale handle must not be
/// able to complete the instance's <i>next</i> life; the token is the version that makes that a detected
/// no-op rather than a silent cross-wiring. See <c>RespMessageBase{T}.TryClaimOutcome</c>.
/// </para>
/// </remarks>
internal interface IRespMessage : IValueTaskSource
{
    /// <summary>The current version; a handle taken now is valid until this instance is reset.</summary>
    short Token { get; }

    /// <summary>Whether the reply may be parsed on the IO thread rather than handed off.</summary>
    bool AllowInlineParsing { get; }

    /// <summary>Block until the outcome is known, or <paramref name="timeout"/> elapses.</summary>
    /// <param name="token">The version this caller holds.</param>
    /// <param name="timeout">How long to wait; <see cref="TimeSpan.Zero"/> waits indefinitely.</param>
    void Wait(short token, TimeSpan timeout);

    /// <summary>Take a reference on the request bytes, so they cannot be recycled while in use.</summary>
    /// <param name="token">The version this caller holds.</param>
    /// <param name="payload">The rendered request.</param>
    /// <param name="recordSent">Whether taking this reference counts as having sent the request.</param>
    /// <returns><see langword="false"/> if the bytes are already gone, or the token is stale.</returns>
    bool TryReserveRequest(short token, out ReadOnlyMemory<byte> payload, bool recordSent = true);

    /// <summary>Release a reference taken by <see cref="TryReserveRequest"/>.</summary>
    void ReleaseRequest();

    /// <summary>Record being handed to a connection, with that connection's byte counters.</summary>
    /// <param name="connection">The connection taking it.</param>
    /// <param name="bytesSent">Bytes the connection had written BEFORE this request.</param>
    /// <param name="bytesReceived">Bytes the connection had read before this request.</param>
    /// <remarks>
    /// On the untyped view because the connection holds operations untyped, and this is the moment only
    /// the connection knows about. The counters are differenced at timeout to answer "did anything move
    /// on this connection while we waited?" - which is the difference between a slow server and a stuck
    /// one, and is why they are snapshotted rather than read later.
    /// </remarks>
    void OnEnqueued(object connection, long bytesSent, long bytesReceived);

    /// <summary>Deliver a reply held in a single span.</summary>
    /// <param name="token">The version this caller holds.</param>
    /// <param name="response">The complete reply frame.</param>
    bool TrySetResult(short token, scoped ReadOnlySpan<byte> response);

    /// <summary>Deliver a reply spanning several buffer segments.</summary>
    /// <param name="token">The version this caller holds.</param>
    /// <param name="response">The complete reply frame.</param>
    bool TrySetResult(short token, in ReadOnlySequence<byte> response);

    /// <summary>Fail the operation.</summary>
    /// <param name="token">The version this caller holds.</param>
    /// <param name="exception">The failure.</param>
    /// <param name="definite">
    /// Whether the pipeline is provably finished with this instance. A server error is definite - the
    /// command was answered. A connection fault is not: the write may still be in flight, so the
    /// instance must not be pooled. See the pooling policy in the design notes, section 6.
    /// </param>
    bool TrySetException(short token, Exception exception, bool definite = false);

    /// <summary>Cancel the operation.</summary>
    /// <param name="token">The version this caller holds.</param>
    /// <param name="cancellationToken">The token to name in the exception, if any.</param>
    bool TrySetCanceled(short token, CancellationToken cancellationToken = default);

    /// <summary>Cancel using the operation's own token; for cancellation callbacks only.</summary>
    void TrySetCanceled();
}
