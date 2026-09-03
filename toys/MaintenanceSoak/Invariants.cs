using System;
using System.Collections.Generic;

namespace StackExchange.Redis.MaintenanceSoak;

/// <summary>
/// The things that must stay true however many cycles run.
/// </summary>
/// <remarks>
/// These are the failure modes a unit test structurally cannot reach, because each needs *repetition* to
/// appear: state that accumulates, a flag that is set and never cleared, a window that is extended once too
/// often. Each violation is recorded with the cycle it happened on rather than throwing, so one run reports
/// everything it found instead of the first thing.
/// </remarks>
internal sealed class Invariants
{
    private readonly List<string> _violations = [];

    public IReadOnlyList<string> Violations => _violations;

    public void Check(bool condition, int cycle, string what)
    {
        if (!condition) _violations.Add($"cycle {cycle}: {what}");
    }

    /// <summary>
    /// Whether the same violation has already been recorded, so a systemic failure reports once per kind
    /// rather than once per cycle.
    /// </summary>
    public bool AlreadySeen(string what) => _violations.Exists(v => v.EndsWith(what, StringComparison.Ordinal));

    public void Record(int cycle, string what)
    {
        if (!AlreadySeen(what)) _violations.Add($"cycle {cycle}: {what}");
    }
}

/// <summary>
/// A memory sample, taken with a forced collection so the number means something.
/// </summary>
internal readonly record struct MemorySample(int Cycle, long Bytes)
{
    public static MemorySample Take(int cycle)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return new MemorySample(cycle, GC.GetTotalMemory(forceFullCollection: true));
    }
}
