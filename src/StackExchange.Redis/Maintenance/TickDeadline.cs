using System;
using System.Threading;

namespace StackExchange.Redis.Maintenance;

/// <summary>
/// A deadline expressed in <see cref="Environment.TickCount"/> terms, where zero means "unset". One place for the
/// three invariants every such field needs: a genuine zero tick count is nudged to one so it cannot read as unset;
/// a deadline is in force only within one span (<see cref="MaxMilliseconds"/>) of now, so the residue of one that
/// passed long ago cannot come back to life when the tick count wraps ~24.8 days later, nor win a comparison against
/// a fresh extension; and extension is monotonic, never shortening a deadline that is already later. A deadline that has merely passed is kept, readable through <see cref="Ticks"/>,
/// so it also records <em>when</em> it passed: fault attribution asks how recently the relaxed window closed, and a
/// held command's cover is stamped from the handoff deadline whether or not the handoff is still running. Used for
/// the relaxed-timeout window, the successor pin, the per-bridge handoff deadline and each held message's cover, which
/// all had their own copies of this before.
/// </summary>
internal struct TickDeadline
{
    private int _ticks;

    /// <summary>
    /// The most a deadline can be set into the future: about six days. A tick count wraps in about 24.8 days, so a
    /// span approaching that would be indistinguishable from the past; and a span that overflows an <c>int</c> of
    /// milliseconds (a buggy or hostile announced window) would otherwise land the deadline in the past outright,
    /// raising a protection that is silently inactive. No maintenance window is days long; clamping is always right.
    /// </summary>
    private const int MaxMilliseconds = int.MaxValue / 4;

    /// <summary>The raw deadline, passed or not, or zero when unset (or so long passed that it has been cleared).</summary>
    public int Ticks => Volatile.Read(ref _ticks);

    public void SetIn(TimeSpan span) => Volatile.Write(ref _ticks, NudgeFromZero(unchecked(Environment.TickCount + Clamp(span))));

    /// <summary>A span as whole milliseconds, never negative and never beyond <see cref="MaxMilliseconds"/>.</summary>
    public static int Clamp(TimeSpan span)
    {
        if (span <= TimeSpan.Zero) return 0;
        var ms = span.TotalMilliseconds;
        return ms >= MaxMilliseconds ? MaxMilliseconds : (int)ms;
    }

    public void Clear() => Volatile.Write(ref _ticks, 0);

    /// <summary>
    /// Moves the deadline out to now plus <paramref name="span"/> unless it is already later than that.
    /// </summary>
    /// <param name="span">How far from now the deadline should be, at least.</param>
    /// <param name="deadline">The deadline now in force, whether or not this call set it.</param>
    /// <returns>Whether this call extended it.</returns>
    public bool TryExtendBy(TimeSpan span, out int deadline)
    {
        var candidate = NudgeFromZero(unchecked(Environment.TickCount + Clamp(span)));
        if (ExtendTo(candidate))
        {
            deadline = candidate;
            return true;
        }
        deadline = Volatile.Read(ref _ticks);
        return false;
    }

    /// <summary>
    /// Moves the deadline out to <paramref name="ticks"/> (a tick count, as <see cref="Ticks"/> reads) unless it is
    /// already later than that; zero means "no deadline" and extends nothing.
    /// </summary>
    /// <returns>Whether this call extended it.</returns>
    public bool ExtendTo(int ticks)
    {
        if (ticks == 0) return false;
        var now = Environment.TickCount;
        while (true)
        {
            var current = Volatile.Read(ref _ticks);
            // a deadline in force is within one span of now, either way; anything else is the residue of a deadline
            // that passed so long ago that the tick count has wrapped past it, and must not win a comparison
            var ahead = unchecked(current - now);
            var inForce = current != 0 && ahead > -MaxMilliseconds && ahead <= MaxMilliseconds;
            if (inForce && unchecked(ticks - current) <= 0) return false;
            if (Interlocked.CompareExchange(ref _ticks, ticks, current) == current) return true;
        }
    }

    /// <summary>
    /// Milliseconds until the deadline, or zero if unset or passed. A passed deadline is kept (see <see cref="Ticks"/>)
    /// until it is so long past that a wrapping tick count could make it look future again, and then cleared.
    /// </summary>
    public int RemainingMilliseconds()
    {
        var deadline = Volatile.Read(ref _ticks);
        if (deadline == 0) return 0;
        var remaining = unchecked(deadline - Environment.TickCount);
        if (remaining > 0 && remaining <= MaxMilliseconds) return remaining;

        // Long passed, or further ahead than any span this type sets - which is what a deadline that passed more
        // than half a wrap ago looks like once the tick count has come round: clear it, but only if nobody has moved
        // it on in the meantime.
        if (remaining < -MaxMilliseconds || remaining > MaxMilliseconds) Interlocked.CompareExchange(ref _ticks, 0, deadline);
        return 0;
    }

    public bool IsActive => RemainingMilliseconds() > 0;

    /// <summary>Zero means "unset" (or "never"), so a tick count that lands on it moves by one.</summary>
    public static int NudgeFromZero(int ticks) => ticks == 0 ? 1 : ticks;
}
