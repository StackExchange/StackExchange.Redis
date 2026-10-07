using System;
using System.Diagnostics;
using System.Threading;

namespace RESPite.Operations;

/// <summary>
/// A last-resort deadline for operations that were given no cancellation of their own.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is not the timeout; it is the net under the timeout.</b> A connection's own bookkeeping should
/// notice a stalled operation long before this does, and when it works nothing here ever fires. What this
/// buys is that a <i>bug</i> in that bookkeeping costs a fault after a couple of minutes instead of a
/// process that waits for ever - which is the difference between a test that names the broken thing and
/// one that has to be caught in a debugger.
/// </para>
/// <para>
/// <b>Why it was needed.</b> <c>RespMessageBase.TrySetTimeout</c> existed but only the synchronous wait
/// could reach it, so a synchronous caller timed out and an asynchronous one did not - it simply never
/// completed. Two separate hangs in the new core's test run were that, and both cost far more to find
/// than they would have if the operation had faulted on its own.
/// </para>
/// <para>
/// <b>Epochs, because a timer per operation is not affordable.</b> A <see cref="CancellationTokenSource"/>
/// with a timeout costs a timer each, and the concurrent scenarios run over a million operations a second.
/// Instead one source covers every operation started within a <see cref="Period"/>, and is cancelled a
/// full <see cref="Backstop"/> after that window closes - so an operation gets between <c>Backstop</c> and
/// <c>Backstop + Period</c>, one timer exists per period rather than per operation, and each operation
/// pays only a registration, which is what it already pays whenever a caller supplies a token.
/// </para>
/// <para>
/// <b>Off unless asked for, because the registration is not free.</b> "Only a registration" turned out to
/// be the largest single cost found on the send path: every token-less operation registered on - and later
/// unregistered from - one process-wide source, so every thread contended for that source's lock twice per
/// command. Measured +9-19% throughput with it off (median of 5, 1M INCR, 6-50 callers). Its value is
/// diagnostic - a bug in the timeout bookkeeping faults instead of hanging - and that is worth most where a
/// hang is hardest to read, so the test suites turn it on; in production the connection's heartbeat already
/// times out what it has written and what is waiting to be.
/// </para>
/// <para>
/// <b>It can cut short a command that was meant to take a long time.</b> This surface exposes no blocking
/// command - there is no <c>BLPOP</c>, and <c>XREAD</c> is deliberately sent without <c>BLOCK</c> - so its
/// own traffic is unaffected. A caller sending a long one as a raw command is not: they should pass a
/// cancellation token, which suppresses this entirely, or raise <see cref="Backstop"/>.
/// </para>
/// </remarks>
internal static class OperationBackstop
{
    /// <summary>How long an operation is given before the net catches it. Zero or less (the default) disables it.</summary>
    internal static TimeSpan Backstop { get; set; } = TimeSpan.Zero;

    /// <summary>How long one epoch's source accepts new operations before it is retired.</summary>
    /// <remarks>Coarse on purpose: it is the slack in the deadline, and slack is free on a net measured in minutes.</remarks>
    internal static TimeSpan Period { get; set; } = TimeSpan.FromSeconds(30);

    private sealed class Epoch
    {
        internal readonly CancellationTokenSource Source;
        internal readonly long RetireAt;

        /// <summary>The settings this epoch was armed with, so a change to them takes effect.</summary>
        /// <remarks>
        /// An epoch is armed once and cannot be re-armed, so without this a change to
        /// <see cref="Backstop"/> would not be felt until the current epoch happened to retire - up to a
        /// whole period later, and with the OLD deadline still in force for everything already on it.
        /// </remarks>
        internal readonly TimeSpan Period;
        internal readonly TimeSpan Backstop;

        internal Epoch(TimeSpan period, TimeSpan backstop)
        {
            Period = period;
            Backstop = backstop;
            RetireAt = Stopwatch.GetTimestamp() + (long)(period.TotalSeconds * Stopwatch.Frequency);

            // Armed AT BIRTH, not when something else retires it. Rotation is driven by traffic, and a
            // stalled connection is exactly the case with no further traffic to drive it - so an epoch
            // that waited to be retired would never fire for the operations that needed it most. The
            // first version did that, and its own test caught it: one operation, nothing after it, and
            // the net never closed. It therefore covers its whole accept window plus the backstop.
            Source = new CancellationTokenSource(period + backstop);
        }
    }

    private static Epoch _epoch = new(Period, Backstop);

    /// <summary>A token that will be cancelled once this operation has had long enough to be a bug.</summary>
    internal static CancellationToken Token
    {
        get
        {
            var backstop = Backstop;
            if (backstop <= TimeSpan.Zero) return default; // disabled: no registration, no net

            var period = Period;
            var epoch = Volatile.Read(ref _epoch);
            if (epoch.Backstop == backstop
                && epoch.Period == period
                && Stopwatch.GetTimestamp() < epoch.RetireAt
                && !epoch.Source.IsCancellationRequested)
            {
                return epoch.Source.Token;
            }

            var replacement = new Epoch(period, backstop);
            if (ReferenceEquals(Interlocked.CompareExchange(ref _epoch, replacement, epoch), epoch))
            {
                // The retired source needs nothing doing to it: it was armed when it was created, so it
                // will fire for whatever is still registered on it whether or not anything else happens.
                // Not disposed either - its timer holds it until it fires, and after that only the
                // registrations of operations still running, which release themselves as they complete.
                return replacement.Source.Token;
            }

            replacement.Source.Dispose(); // somebody else rotated first; theirs wins
            return Volatile.Read(ref _epoch).Source.Token;
        }
    }
}
