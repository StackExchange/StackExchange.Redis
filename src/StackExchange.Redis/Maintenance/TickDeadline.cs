using System;
using System.Threading;

namespace StackExchange.Redis.Maintenance;

/// <summary>
/// A deadline expressed in <see cref="Environment.TickCount"/> terms, where zero means "unset". One place for the
/// three invariants every such field needs: a genuine zero tick count is nudged to one so it cannot read as unset;
/// an observed expiry is cleared (by compare-exchange, so a concurrent extension is not undone), so that a tick count
/// wrapping ~24.8 days later cannot bring a stale deadline back to life; and extension is monotonic, never shortening
/// a deadline that is already later. Used for the relaxed-timeout window, the handoff hold cover and the per-bridge
/// handoff protection, which all had their own copies of this before.
/// </summary>
internal struct TickDeadline
{
    private int _ticks;

    /// <summary>The raw deadline, or zero when unset. For callers that record *when* a window was due to end.</summary>
    public int Ticks => Volatile.Read(ref _ticks);

    public void SetIn(int milliseconds) => Volatile.Write(ref _ticks, NudgeFromZero(unchecked(Environment.TickCount + milliseconds)));

    public void Clear() => Volatile.Write(ref _ticks, 0);

    /// <summary>
    /// Moves the deadline out to now plus <paramref name="milliseconds"/> unless it is already later than that.
    /// </summary>
    /// <param name="milliseconds">How far from now the deadline should be, at least.</param>
    /// <param name="deadline">The deadline now in force, whether or not this call set it.</param>
    /// <returns>Whether this call extended it.</returns>
    public bool TryExtendBy(int milliseconds, out int deadline)
    {
        var candidate = NudgeFromZero(unchecked(Environment.TickCount + milliseconds));
        while (true)
        {
            var current = Volatile.Read(ref _ticks);
            if (current != 0 && unchecked(candidate - current) <= 0)
            {
                deadline = current;
                return false;
            }
            if (Interlocked.CompareExchange(ref _ticks, candidate, current) == current)
            {
                deadline = candidate;
                return true;
            }
        }
    }

    /// <summary>Milliseconds until the deadline, or zero if unset or passed; a passed deadline is cleared.</summary>
    public int RemainingMilliseconds()
    {
        var deadline = Volatile.Read(ref _ticks);
        if (deadline == 0) return 0;
        var remaining = unchecked(deadline - Environment.TickCount);
        if (remaining > 0) return remaining;

        // expired: clear it, but only if nobody has moved it on in the meantime
        Interlocked.CompareExchange(ref _ticks, 0, deadline);
        return 0;
    }

    public bool IsActive => RemainingMilliseconds() > 0;

    private static int NudgeFromZero(int ticks) => ticks == 0 ? 1 : ticks;
}
