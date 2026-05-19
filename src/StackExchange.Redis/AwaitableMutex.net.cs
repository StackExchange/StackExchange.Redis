using System.Threading;
using System.Threading.Tasks;

#if NET
namespace StackExchange.Redis;

internal partial struct AwaitableMutex
{
    private readonly int _timeoutMilliseconds;

    // note: this does not guarantee "fairness", but that's OK for our use-case - we mostly just want
    // a sync+async awaitable mutex, which this does; the .NET Framework version has a hand-written
    // implementation (see .netfx.cx for reasons), which *is* fair, but we'd rather not pay that overhead
    // here. Good-enough-is.
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
#endif
