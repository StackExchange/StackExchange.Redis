using System;
using System.Threading.Tasks;

namespace StackExchange.Redis.FaultInjector.Tests;

/// <summary>
/// Waits for a condition that a real deployment reaches on its own schedule.
/// </summary>
internal static class Poll
{
    public static async Task<bool> UntilAsync(Func<bool> condition, int timeoutMilliseconds = 10_000, int pollMilliseconds = 250)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (true)
        {
            if (condition()) return true;
            if (DateTime.UtcNow > deadline) return false;
            await Task.Delay(pollMilliseconds);
        }
    }
}
