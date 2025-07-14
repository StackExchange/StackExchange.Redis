#if !NET6_0_OR_GREATER
using System;
using System.Threading.Tasks;

namespace StackExchange.Redis.Tests.Helpers;

internal static class TaskExtensions
{
    public static async Task<T> WaitAsync<T>(this Task<T> task, TimeSpan timeout)
    {
        if (task == await Task.WhenAny(task, Task.Delay(timeout)).ForAwait())
        {
            return await task.ForAwait();
        }
        else
        {
            throw new TimeoutException();
        }
    }
}
#endif
