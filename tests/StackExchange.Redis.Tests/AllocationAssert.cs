using System;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Allocation assertions that survive a full-suite run.
/// </summary>
/// <remarks>
/// A single <c>GC.GetAllocatedBytesForCurrentThread</c> window is not reliable on its own: tiered JIT
/// recompilation can promote a method mid-loop and allocate on the measuring thread, which shows up as a
/// flake only under load - the case where it is least welcome. Taking the BEST of several windows keeps
/// the assertion exact rather than adding a tolerance: code that allocates per call allocates in every
/// window, while one-off runtime noise does not.
/// </remarks>
internal static class AllocationAssert
{
    private const int Windows = 5;

    /// <summary>Assert that <paramref name="action"/> allocates nothing per call.</summary>
    internal static void None(Action action, int iterations = 1000, int warmup = 500)
    {
        Assert.Equal(0, Measure(action, iterations, warmup));
    }

    /// <summary>The fewest bytes <paramref name="action"/> allocated across several measurement windows.</summary>
    internal static long Measure(Action action, int iterations = 1000, int warmup = 500)
    {
        for (var i = 0; i < warmup; i++) action();

        var best = long.MaxValue;
        for (var window = 0; window < Windows; window++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < iterations; i++) action();
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            if (allocated < best) best = allocated;
            if (best == 0) break; // cannot do better, and no reason to keep burning time
        }

        return best;
    }
}
