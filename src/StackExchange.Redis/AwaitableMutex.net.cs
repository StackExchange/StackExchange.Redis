using System.Threading;
using System.Threading.Tasks;

// #if NET
namespace StackExchange.Redis;

internal partial struct AwaitableMutex
{
    private readonly int _timeoutMilliseconds;
    private readonly SemaphoreSlim _mutex;

    private partial AwaitableMutex(int timeoutMilliseconds)
    {
        _timeoutMilliseconds = timeoutMilliseconds;
        _mutex = new(1, 1);
    }

    public partial void Dispose() => _mutex?.Dispose();
    public partial bool IsAvailable => _mutex.CurrentCount != 0;
    public partial int TimeoutMilliseconds => _timeoutMilliseconds;

    public partial bool TryTakeInstant() => _mutex.Wait(0);

    public partial ValueTask<bool> TryTakeAsync(CancellationToken cancellationToken)
        => new(_mutex.WaitAsync(_timeoutMilliseconds, cancellationToken));

    public partial bool TryTakeSync() => _mutex.Wait(_timeoutMilliseconds);

    public partial void Release() => _mutex.Release();
}
// #endif
