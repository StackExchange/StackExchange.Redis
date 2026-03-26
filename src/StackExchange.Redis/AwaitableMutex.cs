using System;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Redis;

// abstract over the concept of awaiable mutex between platforms
internal readonly partial struct AwaitableMutex : IDisposable
{
    // ReSharper disable once ConvertToAutoProperty
    public partial int TimeoutMilliseconds { get; }
    public static AwaitableMutex Create(int timeoutMilliseconds) => new(timeoutMilliseconds);

    // define the API first here (think .h file)
    private partial AwaitableMutex(int timeoutMilliseconds);
    public partial void Dispose();
    public partial bool IsAvailable { get; }
    public partial bool TryTakeInstant();
    public partial ValueTask<bool> TryTakeAsync(CancellationToken cancellationToken = default);
    public partial bool TryTakeSync();
    public partial void Release();
}
