using System;

namespace RESPite.Operations;

/// <summary>
/// Somewhere other than the thread-pool for an operation's continuation to run: the thread of a caller that
/// is blocked waiting for it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why.</b> Operations dispatch continuations to the pool, so a synchronous caller blocked on one wakes only
/// when a pool thread runs its completion - which under a saturated pool may be a long time, and is the case
/// the dedicated threads exist for. Running the continuation on the completing thread instead was tried, and
/// deadlocks: the reader ends up running library code that sends, a send can wait on write backpressure, and
/// that only clears when the reader reads. The blocked caller's own thread has neither problem - it is idle,
/// and it is the thread the work belongs to.
/// </para>
/// <para>
/// <b>Contract.</b> <see cref="TryPost"/> must not block and must not run the work itself; it returns false once
/// the sink has stopped accepting (its caller has gone), and the work then goes to the thread-pool as usual.
/// </para>
/// </remarks>
internal interface IContinuationSink
{
    /// <summary>Queue a continuation for the sink's thread, or decline if that call is over.</summary>
    /// <param name="generation">The call it was attached under; a sink is reused across calls, and a post
    /// for an earlier one is declined rather than run in the middle of a later one.</param>
    /// <param name="continuation">The continuation.</param>
    /// <param name="state">Its state.</param>
    bool TryPost(long generation, Action<object?> continuation, object? state);
}
